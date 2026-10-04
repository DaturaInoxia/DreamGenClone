using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Turns the operator's per-render character LoRA selection into the descriptors the ComfyUI graph builder
/// consumes — or refuses the render.
///
/// Identity strategies are SIBLINGS, not alternatives to be switched between: this resolver is only ever asked
/// when the operator selected a LoRA, and a render that selects none returns an empty list without consulting a
/// single repository, so the reference/IP-Adapter route is untouched for every other render.
///
/// Every refusal below is deliberate, and none of them falls back to anything: a render that silently used a
/// different artifact, a different strength, or the wrong checkpoint's LoRA would produce an image of a person
/// the operator did not ask for, and would look exactly like one that honoured the request. The failure names
/// the character and both models so the fix is obvious from the error alone.
/// </summary>
public interface ISceneImageCharacterLoraResolver
{
    /// <summary>
    /// The LoRAs this render must apply, in chain order. Empty means NO LoRA — the graph builder then emits no
    /// <c>LoraLoader</c> node at all.
    /// </summary>
    Task<IReadOnlyList<ResolvedCharacterLora>> ResolveAsync(
        ResolvedImageModel model,
        SceneImageStudioSettings? settings,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The ONE place a character LoRA's trigger tokens are stated in a prompt, shared by every render path.
///
/// <para>
/// Without the token the graph happily loads the LoRA and renders a stranger — a failure indistinguishable from
/// success — so the token is as load-bearing as the LoRA itself. It lives here rather than privately on one handler
/// because two paths now apply character LoRAs, and two copies of "prepend the trigger tokens" would eventually
/// disagree about the separator, the order, or whether to trim.
/// </para>
/// </summary>
public static class CharacterLoraPromptTokens
{
    /// <summary>
    /// States each character's trigger token at the FRONT of the prompt. Order follows the chain, so a
    /// multi-character frame names each character in the order its LoRA is applied.
    ///
    /// <para>
    /// A prompt that already opens with the token of a LoRA this render is applying is REFUSED rather than quietly
    /// de-duplicated. The token is stated here and nowhere else, so its presence in the description means the token
    /// has been applied twice - which is what a round-trip used to do, by restoring the compiled prompt (token
    /// included) beside the LoRA selection that put the token there. Skipping the second one silently would leave the
    /// real question unanswered - which text owns the token - and the render would still not be the one that was
    /// asked for.
    /// </para>
    /// </summary>
    public static string Prepend(string prompt, IReadOnlyList<ResolvedCharacterLora> loras)
    {
        ArgumentNullException.ThrowIfNull(loras);

        var tokens = loras
            .Select(lora => lora.TriggerToken.Trim())
            .Where(token => token.Length > 0)
            .ToList();
        if (tokens.Count == 0)
        {
            return prompt;
        }

        var trimmed = prompt.TrimStart();
        var repeated = tokens.FirstOrDefault(token =>
            trimmed.StartsWith($"{token},", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(token, StringComparison.OrdinalIgnoreCase));
        if (repeated is not null)
        {
            throw new InvalidOperationException(
                $"The prompt already begins with the trigger token '{repeated}' for a LoRA this render is applying, so "
                + "the token would be stated twice. Remove the trigger token from the description: it is added from "
                + "the selected LoRA, so the text must not carry it.");
        }

        return $"{string.Join(", ", tokens)}, {prompt}";
    }
}

public sealed class SceneImageCharacterLoraResolver : ISceneImageCharacterLoraResolver
{
    private readonly ICharacterLoraRepository _loraRepository;
    private readonly ICharacterProfileService _characterProfiles;
    private readonly ILogger<SceneImageCharacterLoraResolver> _logger;

    public SceneImageCharacterLoraResolver(
        ICharacterLoraRepository loraRepository,
        ICharacterProfileService characterProfiles,
        ILogger<SceneImageCharacterLoraResolver> logger)
    {
        _loraRepository = loraRepository;
        _characterProfiles = characterProfiles;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ResolvedCharacterLora>> ResolveAsync(
        ResolvedImageModel model,
        SceneImageStudioSettings? settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var selections = settings?.CharacterLoras;
        if (selections is null || selections.Count == 0)
        {
            // No LoRA is a CONFIGURED state, not a fallback: the render proceeds with the graph it would have
            // had before LoRA identity existed.
            return [];
        }

        var resolved = new List<ResolvedCharacterLora>(selections.Count);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            var artifactId = selection.ArtifactId?.Trim() ?? string.Empty;
            if (artifactId.Length == 0)
            {
                throw new InvalidOperationException(
                    "A character LoRA selection must name a trained artifact. Remove the empty entry or pick an "
                    + "artifact for it.");
            }

            if (!selected.Add(artifactId))
            {
                throw new InvalidOperationException(
                    $"Character LoRA artifact '{artifactId}' is selected twice. Apply each character's LoRA once: "
                    + "loading the same LoRA twice shifts the identity rather than strengthening it.");
            }

            if (selection.Strength is not { } strength || strength <= 0)
            {
                throw new InvalidOperationException(
                    $"Character LoRA artifact '{artifactId}' was selected without a strength. A LoRA applied at a "
                    + "strength nobody chose is a different identity from the trained one, so an explicit positive "
                    + "strength is required.");
            }

            var artifact = await _loraRepository.GetArtifactAsync(artifactId, cancellationToken)
                ?? throw new InvalidOperationException($"Character LoRA artifact '{artifactId}' was not found.");

            var characterName = await ResolveCharacterNameAsync(artifact.CharacterTemplateId, cancellationToken);

            if (artifact.Status != CharacterLoraArtifactStatus.Qualified)
            {
                throw new InvalidOperationException(
                    $"The LoRA for {characterName} is {artifact.Status}, and only a Qualified artifact may be "
                    + "rendered. Qualify it on that character's dataset training page first.");
            }

            // Whether this artifact may be loaded under the checkpoint about to be used is asked of the ARTIFACT,
            // through the same rule the picker filters by. Asking it privately here is exactly what let the picker
            // offer a LoRA this resolver then refused: one dataset can hold N artifacts (one per trained model), and a
            // Krea 2 artifact trained on the raw DiT is DECLARED loadable under the Turbo repack by the operator.
            if (!artifact.LoadsUnder(model.ModelIdentifier))
            {
                throw new InvalidOperationException(
                    $"The LoRA for {characterName} was trained on base model '{artifact.BaseModelId}' and is not "
                    + $"declared loadable under '{model.ModelIdentifier}', which this render uses. A LoRA binds to the "
                    + "checkpoint it was trained against, or to one declared for it on the artifact — train a profile "
                    + "for this model, or declare this model on the artifact.");
            }

            if (string.IsNullOrWhiteSpace(artifact.TriggerToken))
            {
                throw new InvalidOperationException(
                    $"The LoRA for {characterName} has no trigger token, so nothing in the prompt would bind it. "
                    + "The token is part of the dataset the LoRA was trained from.");
            }

            // ComfyUI resolves lora_name against its own loras folder, so it takes the FILE NAME. The artifact
            // records the path the training host published to, which is a Windows path on that host and would be
            // rejected by ComfyUI verbatim.
            var fileName = Path.GetFileName(artifact.FileRelativePath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException(
                    $"The LoRA for {characterName} has no published file, so ComfyUI cannot load it. Re-run the "
                    + "training attempt or repair the artifact's file path.");
            }

            resolved.Add(new ResolvedCharacterLora(
                ArtifactId: artifact.Id,
                CharacterProfileId: artifact.CharacterTemplateId,
                CharacterName: characterName,
                TriggerToken: artifact.TriggerToken,
                FileName: fileName,
                Strength: strength,
                Sha256: artifact.Sha256));
        }

        _logger.LogInformation(
            "Applying {Count} character LoRA(s) to a {Family} render of {Checkpoint}: {Loras}",
            resolved.Count,
            model.SceneImageModelFamily,
            model.ModelIdentifier,
            string.Join(", ", resolved.Select(lora => $"{lora.CharacterName}={lora.FileName}@{lora.Strength}")));

        return resolved;
    }

    /// <summary>
    /// The character's display name, so a refusal names a person. An artifact whose profile id no longer resolves
    /// reports the id rather than inventing a name; an artifact with no id at all reports that honestly too.
    /// </summary>
    private async Task<string> ResolveCharacterNameAsync(
        string characterProfileId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(characterProfileId))
        {
            return "(unregistered character)";
        }

        var profile = await _characterProfiles.GetAsync(characterProfileId, cancellationToken);
        return string.IsNullOrWhiteSpace(profile?.Name) ? characterProfileId : profile!.Name;
    }
}
