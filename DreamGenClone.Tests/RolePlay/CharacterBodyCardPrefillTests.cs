using System.Reflection;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Web.Application.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-122 body-card prefill: the values handed to the operator come from two named sources (the character template's
/// structured attributes and an optional model draft of its description), nothing is written by either, and a field
/// the operator has already answered is never overwritten. The gaps matter as much as the proposals — an invented
/// "none" for body hair, tattoos or pubic hair is exactly what the card exists to prevent.
/// </summary>
public sealed class CharacterBodyCardPrefillTests
{
    private static readonly Guid BeckyTemplateId = Guid.Parse("de351eb3-69d3-421a-a762-79ae8ee183ed");

    [Fact]
    public async Task FromCharacterTemplate_ProposesEveryFieldTheAttributesAnswer_WithTheirProvenance()
    {
        var service = CreateService(FullTemplate());

        var prefill = await service.FromCharacterTemplateAsync(BeckyTemplateId.ToString());

        Assert.Equal(CharacterBodyCardPrefillSource.CharacterAttributes, prefill.Source);
        Assert.Null(prefill.ModelIdentifier);
        Assert.Empty(prefill.Gaps);
        Assert.Equal(CharacterBodyCardFields.All.Count, prefill.Proposals.Count);

        var bodyShape = Proposal(prefill, CharacterBodyCardField.BodyShape);
        Assert.Contains("fuller rear with a soft belly", bodyShape.ProposedValue, StringComparison.Ordinal);
        Assert.Contains("bust full", bodyShape.ProposedValue, StringComparison.Ordinal);
        Assert.Contains("BodyBuild", bodyShape.Provenance, StringComparison.Ordinal);
        Assert.Contains("ButtSize", bodyShape.Provenance, StringComparison.Ordinal);

        Assert.Equal("5'8\"", Proposal(prefill, CharacterBodyCardField.HeightBuild).ProposedValue);
        Assert.Equal("fair, smooth", Proposal(prefill, CharacterBodyCardField.Skin).ProposedValue);
        Assert.Equal("moderate chest hair", Proposal(prefill, CharacterBodyCardField.BodyHair).ProposedValue);
        Assert.Equal("neatly trimmed", Proposal(prefill, CharacterBodyCardField.PubicHair).ProposedValue);
        Assert.Equal("small mole on left cheek", Proposal(prefill, CharacterBodyCardField.ScarsMarks).ProposedValue);
    }

    [Fact]
    public async Task FromCharacterTemplate_TattoosCarryThePlacementCaveat()
    {
        var service = CreateService(FullTemplate());

        var prefill = await service.FromCharacterTemplateAsync(BeckyTemplateId.ToString());

        var tattoos = Proposal(prefill, CharacterBodyCardField.Tattoos);
        Assert.Equal("flower on left wrist", tattoos.ProposedValue);
        // The template records the design; the card needs placement, and the provenance says so rather than guessing.
        Assert.Contains("placement", tattoos.Provenance, StringComparison.Ordinal);
        Assert.True(tattoos.RequiresDecision);
    }

    [Fact]
    public async Task FromCharacterTemplate_ReportsGapsInsteadOfGuessing()
    {
        var template = new TemplateDefinition
        {
            Id = BeckyTemplateId,
            Name = "Becky",
            TemplateType = TemplateType.Character,
            PhysicalAttributes = new PhysicalAttributes { Height = "5'8\"" }
        };
        var service = CreateService(template);

        var prefill = await service.FromCharacterTemplateAsync(BeckyTemplateId.ToString());

        var proposal = Assert.Single(prefill.Proposals);
        Assert.Equal(CharacterBodyCardField.HeightBuild, proposal.Field);
        Assert.Equal(CharacterBodyCardFields.All.Count - 1, prefill.Gaps.Count);

        // The [DECIDE] items are gaps, not "none": an unanswered decision must stay unanswered.
        var bodyHair = Assert.Single(prefill.Gaps, gap => gap.Field == CharacterBodyCardField.BodyHair);
        Assert.True(bodyHair.RequiresDecision);
        Assert.Contains("BodyHair", bodyHair.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(
            prefill.Proposals, proposal => proposal.ProposedValue.Contains("none", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FromCharacterTemplate_RefusesATemplateThatIsNotACharacter()
    {
        var template = new TemplateDefinition
        {
            Id = BeckyTemplateId,
            Name = "Pine clearing",
            TemplateType = TemplateType.Location
        };
        var service = CreateService(template);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.FromCharacterTemplateAsync(BeckyTemplateId.ToString()));

        Assert.Contains("Location", error.Message, StringComparison.Ordinal);
        Assert.Contains("body card belongs to the character template", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FromCharacterTemplate_RefusesAnIdThatIsNotATemplateId()
    {
        var service = CreateService(FullTemplate());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.FromCharacterTemplateAsync("f58f959a-8050-4388-a219-99d2df3446a1"));

        Assert.Contains("character template", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftFromDescription_ProposesOnlyWhatTheDescriptionStates()
    {
        var completion = new CompletionStub(
            """
            {
              "bodyShape": "curvy, full bust",
              "heightBuild": null,
              "skin": "fair, smooth",
              "bodyHair": null,
              "tattoos": "flower on left wrist",
              "scarsMarks": null,
              "pubicHair": "neatly trimmed"
            }
            """);
        var service = CreateService(FullTemplate(), completion);

        var prefill = await service.DraftFromDescriptionAsync(BeckyTemplateId.ToString());

        Assert.Equal(CharacterBodyCardPrefillSource.DescriptionDraft, prefill.Source);
        Assert.Equal("vendor/draft", prefill.ModelIdentifier);
        Assert.Equal(
            [
                CharacterBodyCardField.BodyShape,
                CharacterBodyCardField.Skin,
                CharacterBodyCardField.Tattoos,
                CharacterBodyCardField.PubicHair
            ],
            prefill.Proposals.Select(proposal => proposal.Field));

        // Silence in the description is a gap, and the model's identifier is what the provenance names.
        Assert.Equal(3, prefill.Gaps.Count);
        Assert.Contains("vendor/draft", Proposal(prefill, CharacterBodyCardField.BodyShape).Provenance, StringComparison.Ordinal);
        Assert.Contains(prefill.Gaps, gap => gap.Field == CharacterBodyCardField.BodyHair);
    }

    [Fact]
    public async Task DraftFromDescription_TreatsANonAnswerAsSilence()
    {
        var completion = new CompletionStub(
            """
            {
              "bodyShape": "unknown",
              "heightBuild": "not specified",
              "skin": "fair",
              "bodyHair": "n/a",
              "tattoos": null,
              "scarsMarks": "",
              "pubicHair": "none"
            }
            """);
        var service = CreateService(FullTemplate(), completion);

        var prefill = await service.DraftFromDescriptionAsync(BeckyTemplateId.ToString());

        // Only "fair" and the explicit "none" survive: "unknown"/"n/a"/"not specified" are not facts.
        Assert.Equal(
            [CharacterBodyCardField.Skin, CharacterBodyCardField.PubicHair],
            prefill.Proposals.Select(proposal => proposal.Field));
        Assert.Equal(5, prefill.Gaps.Count);
    }

    [Fact]
    public async Task DraftFromDescription_RefusesATemplateWithNoDescription()
    {
        var template = FullTemplate();
        template.Content = "   ";
        var service = CreateService(template, new CompletionStub("{}"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DraftFromDescriptionAsync(BeckyTemplateId.ToString()));

        Assert.Contains("no description", error.Message, StringComparison.Ordinal);
        Assert.Contains("Content", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftFromDescription_RefusesContentThatIsNotJson()
    {
        var service = CreateService(FullTemplate(), new CompletionStub("I think she is curvy."));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DraftFromDescriptionAsync(BeckyTemplateId.ToString()));

        Assert.Contains("not JSON", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftFromDescription_SendsTheDescriptionAndTheStructuredAttributes()
    {
        var completion = new CompletionStub("{}");
        var service = CreateService(FullTemplate(), completion);

        await service.DraftFromDescriptionAsync(BeckyTemplateId.ToString());

        Assert.NotNull(completion.Request);
        Assert.Contains("Becky", completion.Request!.UserMessage, StringComparison.Ordinal);
        Assert.Contains("curvy", completion.Request.UserMessage, StringComparison.Ordinal);
        Assert.Contains("- Body hair: moderate chest hair", completion.Request.UserMessage, StringComparison.Ordinal);
        // The contract that keeps "none" from being invented travels with every request.
        Assert.Contains("Never answer \"none\"", completion.Request.SystemMessage, StringComparison.Ordinal);
        Assert.Equal("character_body_card_draft", completion.Request.ResponseSchemaName);
    }

    [Fact]
    public async Task Prefill_NeverWritesToTheCard()
    {
        var card = new CharacterBodyCard { CharacterTemplateId = BeckyTemplateId.ToString() };
        var service = CreateService(FullTemplate());

        await service.FromCharacterTemplateAsync(BeckyTemplateId.ToString());

        Assert.False(card.IsReady);
        Assert.Empty(card.BodyShape);
        Assert.Empty(card.PubicHair);
    }

    [Fact]
    public void TryApplyPrefill_AppliesToEmptyFieldsOnly()
    {
        var card = new CharacterBodyCard
        {
            CharacterTemplateId = BeckyTemplateId.ToString(),
            BodyShape = "curvy, as the operator wrote it"
        };

        Assert.False(card.TryApplyPrefill(CharacterBodyCardField.BodyShape, "curvy build"));
        Assert.Equal("curvy, as the operator wrote it", card.BodyShape);

        Assert.True(card.TryApplyPrefill(CharacterBodyCardField.PubicHair, "  neatly trimmed  "));
        Assert.Equal("neatly trimmed", card.PubicHair);

        Assert.False(card.TryApplyPrefill(CharacterBodyCardField.BodyHair, "   "));
        Assert.False(card.TryApplyPrefill(CharacterBodyCardField.BodyHair, null));
    }

    [Fact]
    public void Clone_CarriesEveryPhysicalAttribute()
    {
        // A hand-written copy per editor silently dropped fields (ButtSize and DefaultClothing were already being
        // lost). This walks every writable property, so a new field that Clone misses fails here by name.
        var source = new PhysicalAttributes();
        var index = 0;
        foreach (var property in typeof(PhysicalAttributes)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property is { CanRead: true, CanWrite: true }))
        {
            index++;
            if (property.PropertyType == typeof(string))
            {
                property.SetValue(source, $"{property.Name}-{index}");
            }
            else if (property.PropertyType == typeof(int?))
            {
                property.SetValue(source, index);
            }
            else
            {
                Assert.Fail($"Property '{property.Name}' has type '{property.PropertyType}', which this test does not cover.");
            }
        }

        var clone = source.Clone();

        Assert.NotSame(source, clone);
        foreach (var property in typeof(PhysicalAttributes)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property is { CanRead: true, CanWrite: true }))
        {
            Assert.Equal(property.GetValue(source), property.GetValue(clone));
        }
    }

    [Fact]
    public async Task DraftModelResolver_FailsFastAndNamesWhereToConfigureIt()
    {
        var resolver = new CharacterBodyCardDraftModelResolver(
            new FunctionDefaultsStub(null), new ModelsStub(null), new ProvidersStub(null));

        var error = await Assert.ThrowsAsync<ModelResolutionException>(() => resolver.ResolveAsync());

        Assert.Contains(AppFunction.RolePlayCharacterBodyCardDraft.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains("/model-manager", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DraftModelResolver_UsesItsOwnFunction_NotTheSceneBeatAnalyzers()
    {
        var defaults = new FunctionDefaultsStub(new FunctionModelDefault
        {
            Id = "draft-default",
            FunctionName = AppFunction.RolePlayCharacterBodyCardDraft.ToString(),
            ModelId = "draft-model",
            Temperature = 0.4,
            TopP = 0.9,
            MaxTokens = 1500,
            ThinkingMode = ThinkingMode.Disabled
        });
        var resolver = new CharacterBodyCardDraftModelResolver(
            defaults, new ModelsStub(TextModel()), new ProvidersStub(Provider()));

        var resolved = await resolver.ResolveAsync();

        Assert.Equal(AppFunction.RolePlayCharacterBodyCardDraft, defaults.RequestedFunction);
        Assert.Equal(AppFunction.RolePlayCharacterBodyCardDraft, resolved.Function);
        Assert.Equal("draft-model", resolved.ModelId);
        Assert.Equal(0.4, resolved.Model.Temperature);
        Assert.Equal(StructuredOutputMode.StrictJsonSchema, resolved.StructuredOutputMode);
        // No queue settings are invented for a synchronous function.
        Assert.False(resolved.Model.IsSessionOverride);
    }

    [Fact]
    public void StudioBodyTab_GoesThroughThePrefillServiceAndAppliesToEmptyFieldsOnly()
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "DreamGenClone.Web", "Components", "Pages", "CharacterStudio.razor");
        var source = File.ReadAllText(path);

        Assert.Contains("PrefillService.FromCharacterTemplateAsync(IdentityKey)", source, StringComparison.Ordinal);
        Assert.Contains("PrefillService.DraftFromDescriptionAsync(IdentityKey)", source, StringComparison.Ordinal);
        Assert.Contains("TryApplyPrefill(", source, StringComparison.Ordinal);
        // The prefill handlers must not save: saving stays the operator's explicit action.
        var prefillHandlers = source[source.IndexOf("private Task PrefillFromCharacterAsync", StringComparison.Ordinal)..];
        prefillHandlers = prefillHandlers[..prefillHandlers.IndexOf("private async Task SaveBodyCardAsync", StringComparison.Ordinal)];
        Assert.DoesNotContain("SaveBodyCardAsync(", prefillHandlers, StringComparison.Ordinal);
    }

    private static CharacterBodyCardFieldProposal Proposal(CharacterBodyCardPrefill prefill, CharacterBodyCardField field)
        => Assert.Single(prefill.Proposals, proposal => proposal.Field == field);

    private static TemplateDefinition FullTemplate() => new()
    {
        Id = BeckyTemplateId,
        Name = "Becky",
        TemplateType = TemplateType.Character,
        Content = "Becky is a curvy 34-year-old with fair smooth skin, a trimmed strip of dark pubic hair and a " +
                  "flower tattoo on her left wrist.",
        PhysicalAttributes = new PhysicalAttributes
        {
            Age = "34",
            Height = "5'8\"",
            SkinTone = "fair",
            SkinTexture = "smooth",
            BodyBuild = "average frame",
            Adiposity = "average weight",
            FatDistribution = "fuller rear with a soft belly",
            BustSize = "full",
            WaistSize = "narrow",
            HipSize = "wide",
            ButtSize = "plump",
            BodyHair = "moderate chest hair",
            Tattoos = "flower on left wrist",
            DistinguishingMarks = "small mole on left cheek",
            PubicHair = "neatly trimmed"
        }
    };

    private static ICharacterBodyCardPrefillService CreateService(
        TemplateDefinition template, ISynchronousStructuredTextCompletionClient? completion = null)
        => new CharacterBodyCardPrefillService(
            new TemplateServiceStub(template),
            new DraftModelResolverStub(),
            completion ?? new CompletionStub("{}"));

    private static RegisteredModel TextModel() => new()
    {
        Id = "draft-model",
        ProviderId = "draft-provider",
        ModelIdentifier = "vendor/draft",
        DisplayName = "Draft Model",
        ModelKind = ModelKind.Text,
        IsEnabled = true,
        SupportsThinkingControl = true,
        StructuredOutputMode = StructuredOutputMode.StrictJsonSchema,
        MaximumOutputTokens = 4096
    };

    private static Provider Provider() => new()
    {
        Id = "draft-provider",
        Name = "Provider",
        BaseUrl = "https://provider.example",
        ChatCompletionsPath = "/v1/chat/completions",
        TimeoutSeconds = 120,
        IsEnabled = true
    };

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "DreamGenClone.sln")))
                return current.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the DreamGenClone repository root.");
    }

    private sealed class TemplateServiceStub(TemplateDefinition template) : ITemplateService
    {
        public Task<IReadOnlyList<TemplateDefinition>> GetAllAsync(
            TemplateType? templateType = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TemplateDefinition>>([template]);

        public Task<TemplateDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<TemplateDefinition?>(id == template.Id ? template : null);

        public Task<TemplateDefinition> SaveAsync(TemplateDefinition value, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateImagePathAsync(Guid id, string imagePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class DraftModelResolverStub : ICharacterBodyCardDraftModelResolver
    {
        public Task<ResolvedStructuredTextFunction> ResolveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ResolvedStructuredTextFunction(
                AppFunction.RolePlayCharacterBodyCardDraft,
                "draft-model",
                "draft-provider",
                new ResolvedModel(
                    "https://provider.example",
                    "/v1/chat/completions",
                    120,
                    null,
                    "vendor/draft",
                    0.4,
                    0.9,
                    1500,
                    "Provider",
                    IsSessionOverride: false)
                {
                    SupportsThinkingControl = true,
                    ThinkingMode = ThinkingMode.Disabled
                },
                StructuredOutputMode.StrictJsonSchema));
    }

    private sealed class CompletionStub(string response) : ISynchronousStructuredTextCompletionClient
    {
        public StructuredTextCompletionRequest? Request { get; private set; }

        public Task<StructuredTextCompletionResult> GenerateAsync(
            ResolvedStructuredTextFunction function,
            StructuredTextCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new StructuredTextCompletionResult(
                response, function.Model.ModelIdentifier, "stop", TimeSpan.Zero));
        }
    }

    private sealed class FunctionDefaultsStub(FunctionModelDefault? value) : IFunctionDefaultRepository
    {
        public AppFunction? RequestedFunction { get; private set; }

        public Task<FunctionModelDefault?> GetByFunctionAsync(
            AppFunction function, CancellationToken cancellationToken = default)
        {
            RequestedFunction = function;
            return Task.FromResult(value);
        }

        public Task<FunctionModelDefault> SaveAsync(
            FunctionModelDefault functionDefault, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<FunctionModelDefault>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<FunctionModelDefault>> GetByModelIdAsync(
            string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteByFunctionAsync(AppFunction function, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ModelsStub(RegisteredModel? value) : IRegisteredModelRepository
    {
        public Task<RegisteredModel?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(value);

        public Task<RegisteredModel> SaveAsync(RegisteredModel model, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<RegisteredModel>> GetByProviderIdAsync(
            string providerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<RegisteredModel>> GetAllEnabledAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsByProviderAndIdentifierAsync(
            string providerId, string modelIdentifier, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ProvidersStub(Provider? value) : IProviderRepository
    {
        public Task<Provider?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(value);

        public Task<Provider> SaveAsync(Provider provider, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<Provider>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
