using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-135 B135-015/B135-016 — the Playground's compile step.
///
/// <para>
/// Two of these matter more than the rest. <see cref="DoesNotRepairItsOwnOutput"/> encodes the decision that the
/// compiler must NOT clean up after a model that ignored its instructions: the fence the model emitted is the signal the
/// cell exists to measure, and a compiler that silently stripped it would make a broken cell look green. And
/// <see cref="TellsTheModelWhichAxesAreCarriedStructurally"/> is where the "pose text does not work with more than one
/// person" finding becomes an instruction instead of an unwritten expectation.
/// </para>
/// </summary>
public sealed class ImageCellPromptCompilerTests
{
    private const string Model = "qwen3.5-vl";

    private static ImageCompilerProfile Profile(
        ImagePoseInText poseInText = ImagePoseInText.Full,
        int minChars = 20,
        int maxChars = 600) => new()
    {
        Id = "p-juggernaut",
        CheckpointIdentifier = "juggernautXL_ragnarok.safetensors",
        DisplayName = "Juggernaut XL Ragnarok",
        Family = SceneImageModelFamily.Sdxl,
        PromptDialect = SceneImagePromptDialect.SdxlNaturalLanguage,
        MinChars = minChars,
        MaxChars = maxChars,
        MaxTokens = 100,
        PoseInText = poseInText,
        Negative = string.Empty,
        SystemPrompt = SceneImageCompilerSystemPrompts.NaturalLanguageBeat,
        RequiredComponentsJson = """["subject","framing","lighting"]""",
        ForbiddenTokensJson = """["story-name"]""",
    };

    private static ImageCellCompileInput Input(
        string direction = "Woman laying on bed, seductive look, hands touching herself",
        ImageCompilerProfile? profile = null,
        long? seed = null,
        IReadOnlyList<ImageCellBinding>? bindings = null) =>
        new(direction, profile ?? Profile(), new ImageCellCompilerLlmSettings(Model, 0, seed), bindings ?? []);

    private static ImageCellPromptCompiler NewCompiler(
        string response,
        out RecordingCompletionClient client,
        string resolvedModel = Model,
        double resolvedTemperature = 0)
    {
        client = new RecordingCompletionClient(response);
        return new ImageCellPromptCompiler(
            client,
            new PinnedModelResolver(resolvedModel, resolvedTemperature));
    }

    // ---- the compile itself -------------------------------------------------------------------------------

    [Fact]
    public async Task CompilesWithTheCheckpointsOwnInstructions()
    {
        // The system prompt comes from the PROFILE, never composed here. That is what makes "one place a compiler
        // instruction can come from" true, and it is why the assertion is equality rather than a substring.
        var compiler = NewCompiler("a woman lying on a bed in warm lamp light, 35mm", out var client);
        var profile = Profile();

        var result = await compiler.CompileAsync(Input(profile: profile));

        Assert.Equal(profile.SystemPrompt, result.SystemPrompt);
        Assert.Equal(profile.SystemPrompt, client.SystemPrompt);
    }

    [Fact]
    public async Task TrimsSurroundingWhitespaceAndNothingElse()
    {
        var compiler = NewCompiler("  a woman lying on a bed in warm lamp light\n\n", out _);

        var result = await compiler.CompileAsync(Input());

        Assert.Equal("a woman lying on a bed in warm lamp light", result.CompiledPrompt);
    }

    [Fact]
    public async Task TheCellDirectionIsTheOnlySubjectMatter()
    {
        var compiler = NewCompiler("a woman lying on a bed, one hand on her thigh", out var client);

        await compiler.CompileAsync(Input());

        Assert.Contains("Woman laying on bed, seductive look, hands touching herself", client.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("juggernautXL_ragnarok.safetensors", client.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordsWhatItSentAndWhatAnsweredIt()
    {
        // The run's evidence has to name the compiler that drafted the prompt, or a red cell could be a bad compiler.
        var compiler = NewCompiler("a woman lying on a bed", out _);

        var result = await compiler.CompileAsync(Input());

        Assert.Equal(Model, result.ModelIdentifier);
        Assert.Equal(0, result.Temperature);
        Assert.Contains("USER DIRECTION", result.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotRepairItsOwnOutput()
    {
        // A fenced, three-line answer is returned as it arrived. The prompt layer then fails it, which is the point: a
        // compiler that tidied this up would hide a model that ignored its instructions.
        var fenced = "```\na woman lying on a bed\nwarm lamp light\n```";
        var compiler = NewCompiler(fenced, out _);

        var result = await compiler.CompileAsync(Input());

        Assert.Equal(fenced, result.CompiledPrompt);
        Assert.Contains("```", result.CompiledPrompt, StringComparison.Ordinal);
        Assert.Contains('\n', result.CompiledPrompt);
    }

    // ---- the bindings reach the instruction ---------------------------------------------------------------

    [Fact]
    public async Task TellsTheModelWhichAxesAreCarriedStructurally()
    {
        // The declared binding matrix is what makes a compiled prompt correct: describing a pose the ControlNet already
        // supplies creates a second, conflicting source for the same axis.
        var compiler = NewCompiler("a woman lying on a bed", out var client);

        await compiler.CompileAsync(Input(bindings:
        [
            new ImageCellBinding(ImageBindingAxis.Pose, ImageBindingMode.Adapter, Strategy: "OpenPose"),
            new ImageCellBinding(ImageBindingAxis.Lighting, ImageBindingMode.Text),
        ]));

        Assert.Contains("do NOT describe these in words", client.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("Pose via Adapter", client.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("AXES CARRIED BY THIS TEXT: Lighting", client.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenTheTextCarriesEveryAxis()
    {
        var compiler = NewCompiler("a woman lying on a bed", out var client);

        await compiler.CompileAsync(Input());

        Assert.Contains("AXES CARRIED BY THIS TEXT: none declared", client.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("do NOT describe these in words", client.UserPrompt, StringComparison.Ordinal);
    }

    // ---- the checkpoint's measured limits become instructions ----------------------------------------------

    [Theory]
    [InlineData(ImagePoseInText.Forbidden, "must not be described")]
    [InlineData(ImagePoseInText.SimpleOnly, "simple single-subject")]
    [InlineData(ImagePoseInText.Full, "complex and multi-person")]
    public async Task StatesTheCheckpointsPoseCapability(ImagePoseInText poseInText, string expected)
    {
        // This is the measured finding (BigLust/Juggernaut: pose text essentially does not work, worse with more than one
        // person) carried into the instruction, so the model is told the checkpoint's limit instead of being left to
        // discover it by producing an image with one person in it.
        var compiler = NewCompiler("a woman lying on a bed", out var client);

        await compiler.CompileAsync(Input(profile: Profile(poseInText)));

        Assert.Contains(expected, client.UserPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StatesTheBudgetAndTheForbiddenCategories()
    {
        var compiler = NewCompiler("a woman lying on a bed", out var client);

        await compiler.CompileAsync(Input(profile: Profile(minChars: 40, maxChars: 700)));

        Assert.Contains("between 40 and 700 characters", client.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("subject, framing, lighting", client.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("story-name", client.UserPrompt, StringComparison.Ordinal);
    }

    // ---- refusals -----------------------------------------------------------------------------------------

    [Fact]
    public async Task RefusesAnEmptyUserDirection()
    {
        var compiler = NewCompiler("a woman lying on a bed", out _);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(Input("   ")));

        Assert.Contains("nothing to compile", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesADeclaredSeedItCannotHonour()
    {
        // The seed gate. ICompletionClient has no seed parameter, so a run that declared one would record a seed it never
        // sent - reproducible-looking and not reproducible.
        var compiler = NewCompiler("a woman lying on a bed", out _);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(Input(seed: 42)));

        Assert.Contains("cannot honour it", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("42", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAProfileWithNoInstructions()
    {
        var profile = Profile();
        profile.SystemPrompt = string.Empty;
        var compiler = NewCompiler("a woman lying on a bed", out _);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(Input(profile: profile)));

        Assert.Contains("compiler instructions", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesADifferentModelThanTheOneItPinned()
    {
        var compiler = NewCompiler("a woman lying on a bed", out _, resolvedModel: "some-other-vl");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(Input()));

        Assert.Contains(Model, error.Message, StringComparison.Ordinal);
        Assert.Contains("some-other-vl", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesADifferentTemperatureThanTheOneItPinned()
    {
        // Temperature is a declared run variable. Compiling at one nobody chose makes the cell's result unattributable.
        var compiler = NewCompiler("a woman lying on a bed", out _, resolvedTemperature: 0.7);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(Input()));

        Assert.Contains("Temperature", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesAnEmptyModelResponse()
    {
        var compiler = NewCompiler("   ", out _);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(Input()));

        Assert.Contains("empty prompt", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- doubles ------------------------------------------------------------------------------------------

    private sealed class RecordingCompletionClient(string response) : ICompletionClient
    {
        public string? SystemPrompt { get; private set; }

        public string? UserPrompt { get; private set; }

        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
            string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            SystemPrompt = systemMessage;
            UserPrompt = userMessage;
            return Task.FromResult((response, (string?)null));
        }

        public Task<(string Content, string? Reasoning)> GenerateWithReasoningAsync(
            string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult((response, (string?)null));

        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(
            string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(string Content, string? Reasoning)> StreamGenerateWithReasoningAsync(
            string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult(response);

        public Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult(response);

        public Task<string> StreamGenerateAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> StreamGenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? decryptedApiKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<(bool Success, string Message)> CheckModelHealthAsync(string providerBaseUrl, string chatCompletionsPath, int timeoutSeconds, string? decryptedApiKey, string modelIdentifier, CancellationToken cancellationToken = default)
            => Task.FromResult((true, "ok"));
    }

    /// <summary>
    /// Only the prompt-drafting resolution is exercised; everything else throws loudly rather than returning a plausible
    /// substitute, so a test that accidentally reaches another path fails instead of quietly passing.
    /// </summary>
    private sealed class PinnedModelResolver(string modelIdentifier, double temperature) : IModelResolutionService
    {
        public Task<ResolvedModel> ResolveImagePromptModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ResolvedModel(
                "http://localhost:1234",
                "/v1/chat/completions",
                300,
                null,
                modelIdentifier,
                temperature,
                1,
                4096,
                "Local",
                false));

        public Task<ResolvedModel> ResolveAsync(
            AppFunction function, string? sessionModelId = null, double? sessionTemperature = null,
            double? sessionTopP = null, int? sessionMaxTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedImageModel> ResolveImageModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelAsync(
            string? sessionOverrideId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedImageModel> ResolveImageModelByIdAsync(
            string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ResolvedIdentityImageModel> ResolveIdentityImageModelByIdAsync(
            string modelId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SceneImageModelChoice>> ListSceneImageModelsAsync(
            bool identityCapableOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
