using System.Net;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Models;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-167: the two configuration carriers the plan called for — the prompt budget as a single-owner TOKEN resolved from
/// the checkpoint profile, and the repetition penalty as a persisted, UI-editable function setting.
/// </summary>
public sealed class ImageCompilerConfigurationCarrierTests
{
    // ---- the prompt budget ---------------------------------------------------------------------------------

    [Fact]
    public void BudgetToken_IsResolvedFromTheProfilesOwnValue()
    {
        var text = SceneImageCompilerSystemPrompts.NaturalLanguageCanonical;

        Assert.True(SceneImageCompilerSystemPrompts.HasBudget(text));
        var resolved = SceneImageCompilerSystemPrompts.ApplyBudget(text, 3400);

        Assert.False(SceneImageCompilerSystemPrompts.HasBudget(resolved));
        Assert.Contains("under 3400 characters", resolved, StringComparison.Ordinal);
        // The audited 800 is no longer in the text at all: two numbers cannot disagree when only one exists.
        Assert.DoesNotContain("under 800 characters", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void BudgetToken_ResolvesInEveryFamilyInstructionText()
    {
        // The families that HAVE an instruction text (a video family is refused by For, deliberately).
        foreach (var family in (SceneImageModelFamily[])
        [
            SceneImageModelFamily.Sdxl, SceneImageModelFamily.Flux,
            SceneImageModelFamily.QwenImage21, SceneImageModelFamily.Krea2, SceneImageModelFamily.Api
        ])
        {
            foreach (var canonical in (bool[])[true, false])
            {
                var text = SceneImageCompilerSystemPrompts.For(family, canonical);
                var resolved = SceneImageCompilerSystemPrompts.ApplyBudget(text, 500);
                Assert.False(
                    SceneImageCompilerSystemPrompts.HasBudget(resolved),
                    $"Family '{family}' (canonical: {canonical}) still carries an unresolved budget token.");
            }
        }
    }

    [Fact]
    public void BudgetResolution_RefusesANonPositiveBudget()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SceneImageCompilerSystemPrompts.ApplyBudget(SceneImageCompilerSystemPrompts.NaturalLanguageCanonical, 0));

        Assert.Contains("must be positive", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyBudget_LeavesATextWithoutTheTokenUntouched()
    {
        Assert.Equal("no budget here", SceneImageCompilerSystemPrompts.ApplyBudget("no budget here", 800));
    }

    // ---- the repetition penalty ----------------------------------------------------------------------------

    [Theory]
    [InlineData(2.5)]
    [InlineData(-2.5)]
    public void PresencePenalty_IsRefusedOutsideTheProviderRange(double penalty)
    {
        var field = new FunctionModelDefault
        {
            FunctionName = AppFunction.RolePlayCharacterBodyCardDraft.ToString(),
            ModelId = "model-1",
            PresencePenalty = penalty
        };

        var error = field.ValidateCharacterBodyCardDraftConfiguration();

        Assert.NotNull(error);
        Assert.Contains("Presence penalty", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void PresencePenalty_AcceptsAnInRangeValueAndBlank()
    {
        var configured = new FunctionModelDefault
        {
            FunctionName = AppFunction.RolePlayCharacterBodyCardDraft.ToString(),
            ModelId = "model-1",
            PresencePenalty = 1.5
        };
        Assert.Null(configured.ValidateCharacterBodyCardDraftConfiguration());

        configured.PresencePenalty = null;
        Assert.Null(configured.ValidateCharacterBodyCardDraftConfiguration());
    }

    [Fact]
    public async Task ClientSendsThePenaltyOnlyWhenTheFunctionDeclaresOne()
    {
        var withPenalty = Resolve(1.5);
        var withoutPenalty = Resolve(null);

        var withBody = await PostAsync(withPenalty);
        var withoutBody = await PostAsync(withoutPenalty);

        Assert.Equal(1.5, withBody.GetProperty("presence_penalty").GetDouble());
        // Omitted entirely, so a provider that rejects the parameter sees the request it always saw.
        Assert.False(withoutBody.TryGetProperty("presence_penalty", out _));
    }

    private static async Task<JsonElement> PostAsync(ResolvedModel model)
    {
        string? body = null;
        var handler = new CapturingHandler(content => body = content);
        var client = new CompletionClient(
            new FakeHttpClientFactory(handler),
            new FakeEncryption(),
            NullLogger<CompletionClient>.Instance);

        await client.GenerateWithReasoningAsync("system", "user", model, CancellationToken.None);

        Assert.NotNull(body);
        return JsonDocument.Parse(body!).RootElement;
    }

    private static ResolvedModel Resolve(double? presencePenalty) => new(
        "https://api.together.ai",
        "/v1/chat/completions",
        30,
        null,
        "deepseek-test",
        0.3,
        0.9,
        8000,
        "TogetherAI",
        false)
    {
        PresencePenalty = presencePenalty
    };

    private sealed class CapturingHandler(Action<string> capture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                capture(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[]
                    {
                        new
                        {
                            message = new { role = "assistant", content = "{\"ok\":true}", reasoning_content = (string?)null },
                            finish_reason = "stop"
                        }
                    }
                }))
            };
        }
    }

    private sealed class FakeEncryption : IApiKeyEncryptionService
    {
        public string Encrypt(string plainTextApiKey) => plainTextApiKey;
        public string Decrypt(string encryptedApiKey) => encryptedApiKey;
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);
        public HttpClient CreateClient(string name) => _client;
    }

    // ---- subject visibility --------------------------------------------------------------------------------

    [Fact]
    public void CameraHolderVisibility_IsPartiallyVisibleOnlyWhenContactsPutPartsInFrame()
    {
        var withoutParts = Projection(parts: null);
        var withParts = Projection(parts: "hands (grip p0)");

        Assert.Equal(SceneSubjectVisibility.OutOfFrame, withoutParts.CameraHolderVisibility);
        Assert.Equal(SceneSubjectVisibility.PartiallyVisible, withParts.CameraHolderVisibility);
    }

    private static SceneImagePovProjection.Projection Projection(string? parts) => new(
        "Dean",
        false,
        "the shed",
        false,
        false,
        null,
        null,
        null,
        [],
        [],
        [],
        null,
        CameraHolderParts: parts);
}
