using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>The render, after whatever character LoRAs it selected have been applied to it.</summary>
public sealed record CharacterLoraApplication(ResolvedImageModel Model, string Prompt);

/// <summary>
/// Applies a render's selected character LoRAs: the chain goes on the MODEL, and each character's trigger token goes
/// into the PROMPT.
///
/// <para>
/// <b>Why both, together.</b> Loading the LoRA without stating its trigger token renders a stranger - a failure
/// indistinguishable from success - so the two edits belong to one decision and are made in one place. Two render
/// paths select character LoRAs (the studio's and the asset creator's), and this is where that decision lives so they
/// cannot drift.
/// </para>
///
/// <para>
/// Extracted from the job handler because a handler with nineteen constructor dependencies cannot be tested for this
/// one behaviour without a fixture larger than the behaviour.
/// </para>
/// </summary>
public static class CharacterLoraRenderApplication
{
    public static async Task<CharacterLoraApplication> ApplyAsync(
        ResolvedImageModel model,
        string prompt,
        IReadOnlyList<Models.SceneImageCharacterLoraSelection>? selections,
        ISceneImageCharacterLoraResolver? resolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // No selection is a CONFIGURED state, not a fallback: the render proceeds with the graph it would have had
        // before LoRA identity existed, and nothing about it changes.
        if (selections is not { Count: > 0 })
        {
            return new CharacterLoraApplication(model, prompt);
        }

        var loraResolver = resolver
            ?? throw new InvalidOperationException(
                "This render selects character LoRA(s), but the character LoRA resolver is not available, so the "
                + "selected identity cannot be applied. Rendering without it would produce a different person.");

        var loras = await loraResolver.ResolveAsync(
            model,
            new Models.SceneImageStudioSettings { CharacterLoras = selections.ToList() },
            cancellationToken);

        if (loras.Count == 0)
        {
            // A selection that resolved to no artifact is NOT half-applied: no chain, and therefore no trigger tokens
            // either. Tokens with no LoRA would name a character nothing binds to.
            return new CharacterLoraApplication(model, prompt);
        }

        return new CharacterLoraApplication(
            model with { Loras = loras },
            CharacterLoraPromptTokens.Prepend(prompt, loras));
    }
}
