using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class QwenSceneImageEditPromptCompilerTests
{
    private readonly QwenSceneImageEditPromptCompiler _compiler = new();

    [Fact]
    public void BuildMessages_UsesVersionedSchemaAndPreservesClarificationHistory()
    {
        var messages = _compiler.BuildMessages(new SceneImageEditCompilerContext(
            "Make the foreground woman's shirt red.",
            ["The subject is the woman nearest the window."]));

        Assert.Equal(QwenSceneImageEditPromptCompiler.SchemaVersion, messages.SchemaVersion);
        Assert.Equal(QwenSceneImageEditPromptCompiler.SystemPromptVersion, messages.SystemPromptVersion);
        Assert.Equal(QwenSceneImageEditPromptCompiler.ResponseSchemaName, messages.ResponseSchemaName);
        Assert.Contains("visible locators", messages.SystemMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The user's request is authoritative", messages.SystemMessage, StringComparison.Ordinal);
        Assert.Contains("Never reject a request merely because it changes a category named in the preservation list", messages.SystemMessage, StringComparison.Ordinal);
        Assert.Contains("foreground woman's shirt", messages.UserMessage, StringComparison.Ordinal);
        Assert.Contains("nearest the window", messages.UserMessage, StringComparison.Ordinal);
        Assert.False(messages.ResponseSchema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void BuildMessages_EmptyIntent_Fails()
    {
        Assert.Throws<ArgumentException>(() => _compiler.BuildMessages(new SceneImageEditCompilerContext(" ", [])));
    }

    [Fact]
    public void Parse_ReadyResult_ReturnsExecutableGroundedContract()
    {
        var result = _compiler.Parse(ReadyJson());

        Assert.Equal(SceneImageEditCompilationResultStatus.Ready, result.Status);
        Assert.Single(result.Targets);
        Assert.Equal("foreground-woman", result.Targets[0].Key);
        Assert.Equal("woman in blue shirt at front left", result.Targets[0].VisibleLocator);
        Assert.NotNull(result.Targets[0].Region);
        Assert.Equal("Change the blue shirt to red while preserving all other visible details.", result.CompiledPrompt);
    }

    [Fact]
    public void Parse_ClarificationResult_HasNoExecutablePrompt()
    {
        var result = _compiler.Parse(ResultJson("clarification_required", "Which of the two women should be changed?", null, null));

        Assert.Equal(SceneImageEditCompilationResultStatus.ClarificationRequired, result.Status);
        Assert.Equal("Which of the two women should be changed?", result.ClarificationQuestion);
        Assert.Null(result.CompiledPrompt);
    }

    [Fact]
    public void Parse_InvalidResult_HasNoExecutablePrompt()
    {
        var result = _compiler.Parse(ResultJson("invalid", null, "No visibly standing woman appears in the image.", null));

        Assert.Equal(SceneImageEditCompilationResultStatus.Invalid, result.Status);
        Assert.Equal("No visibly standing woman appears in the image.", result.InvalidReason);
        Assert.Null(result.CompiledPrompt);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("[]")]
    public void Parse_MalformedShape_Fails(string response)
    {
        Assert.Throws<InvalidOperationException>(() => _compiler.Parse(response));
    }

    [Fact]
    public void Parse_ExtraRootField_Fails()
    {
        using var document = JsonDocument.Parse(ReadyJson());
        var root = document.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone());
        root["inventedFact"] = JsonSerializer.SerializeToElement("secret relationship");

        Assert.Throws<InvalidOperationException>(() => _compiler.Parse(JsonSerializer.Serialize(root)));
    }

    [Fact]
    public void Parse_MissingRequiredRootField_Fails()
    {
        using var document = JsonDocument.Parse(ReadyJson());
        var root = document.RootElement.EnumerateObject()
            .Where(item => item.Name != "preserve")
            .ToDictionary(item => item.Name, item => item.Value.Clone());

        Assert.Throws<InvalidOperationException>(() => _compiler.Parse(JsonSerializer.Serialize(root)));
    }

    [Theory]
    [InlineData("unsupported-v2", "ready")]
    [InlineData("scene-image-edit-compiler-v1", "unknown")]
    public void Parse_UnsupportedVersionOrStatus_Fails(string schemaVersion, string status)
    {
        var json = ReadyJson()
            .Replace(QwenSceneImageEditPromptCompiler.SchemaVersion, schemaVersion, StringComparison.Ordinal)
            .Replace("\"status\":\"ready\"", $"\"status\":\"{status}\"", StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => _compiler.Parse(json));
    }

    [Fact]
    public void Parse_ReadyWithoutVisibleTarget_Fails()
    {
        Assert.Throws<InvalidOperationException>(() => _compiler.Parse(ResultJson("ready", null, null, "Change it.")));
    }

    [Fact]
    public void Parse_ClarificationWithStrayPrompt_NormalizesToClarification()
    {
        var result = _compiler.Parse(ResultJson("clarification_required", "Which one?", null, "Guess and change one."));

        Assert.Equal(SceneImageEditCompilationResultStatus.ClarificationRequired, result.Status);
        Assert.Equal("Which one?", result.ClarificationQuestion);
        Assert.Null(result.CompiledPrompt);
        Assert.Null(result.InvalidReason);
    }

    [Fact]
    public void Parse_OutOfBoundsRegion_Fails()
    {
        var json = ReadyJson().Replace("\"width\":0.30", "\"width\":0.90", StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => _compiler.Parse(json));
    }

    [Fact]
    public void Parse_DuplicateTargetKey_Fails()
    {
                var json = """
                        {
                            "schemaVersion":"scene-image-edit-compiler-v1",
                            "status":"ready",
                            "sourceSummary":"Two women stand in a living room.",
                            "targets":[
                                {"key":"woman","visibleLocator":"woman at left","region":null},
                                {"key":"woman","visibleLocator":"woman at right","region":null}
                            ],
                            "requestedChanges":["Change the selected shirt to red."],
                            "preserve":["Preserve all other visible details."],
                            "clarificationQuestion":null,
                            "invalidReason":null,
                            "compiledPrompt":"Change the selected shirt to red."
                        }
                        """;

        Assert.Throws<InvalidOperationException>(() => _compiler.Parse(json));
    }

    [Fact]
    public void Parse_InvalidWithEmptyCompiledPromptAndStrayQuestion_NormalizesToInvalid()
    {
        var result = _compiler.Parse(ResultJson("invalid", "Are you sure you want to add pubic hair?", "The request to add pubic hair is not feasible.", ""));

        Assert.Equal(SceneImageEditCompilationResultStatus.Invalid, result.Status);
        Assert.Equal("The request to add pubic hair is not feasible.", result.InvalidReason);
        Assert.Null(result.CompiledPrompt);
        Assert.Null(result.ClarificationQuestion);
    }

    [Fact]
    public void Parse_ClarificationWithEmptyCompiledPrompt_NormalizesToNull()
    {
        var result = _compiler.Parse(ResultJson("clarification_required", "Which visible woman should be edited?", null, ""));

        Assert.Equal(SceneImageEditCompilationResultStatus.ClarificationRequired, result.Status);
        Assert.Equal("Which visible woman should be edited?", result.ClarificationQuestion);
        Assert.Null(result.CompiledPrompt);
        Assert.Null(result.InvalidReason);
    }

    [Fact]
    public void Parse_ReadyWithStrayInvalidReason_NormalizesToReady()
    {
        var json = ReadyJson().Replace("\"invalidReason\":null", "\"invalidReason\":\"Ignore this stray reason.\"", StringComparison.Ordinal);

        var result = _compiler.Parse(json);

        Assert.Equal(SceneImageEditCompilationResultStatus.Ready, result.Status);
        Assert.NotNull(result.CompiledPrompt);
        Assert.Null(result.InvalidReason);
        Assert.Null(result.ClarificationQuestion);
    }

    [Fact]
    public void Parse_InvalidWithoutReason_StillFails()
    {
        Assert.Throws<InvalidOperationException>(() => _compiler.Parse(ResultJson("invalid", null, null, "")));
    }

    [Fact]
    public void Parse_ClarificationWithoutQuestion_StillFails()
    {
        Assert.Throws<InvalidOperationException>(() => _compiler.Parse(ResultJson("clarification_required", null, null, null)));
    }

    [Fact]
    public void Parse_NonStringCompiledPrompt_StillFails()
    {
        var json = ReadyJson().Replace(
            "\"compiledPrompt\":\"Change the blue shirt to red while preserving all other visible details.\"",
            "\"compiledPrompt\":42",
            StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => _compiler.Parse(json));
    }

    private static string ReadyJson() => """
        {
          "schemaVersion":"scene-image-edit-compiler-v1",
          "status":"ready",
          "sourceSummary":"Two women stand in a living room.",
          "targets":[{"key":"foreground-woman","visibleLocator":"woman in blue shirt at front left","region":{"x":0.20,"y":0.10,"width":0.30,"height":0.70}}],
          "requestedChanges":["Change the blue shirt to red."],
          "preserve":["Preserve both women's visible identity, pose, room, lighting, and composition."],
          "clarificationQuestion":null,
          "invalidReason":null,
          "compiledPrompt":"Change the blue shirt to red while preserving all other visible details."
        }
        """;

    private static string ResultJson(string status, string? clarification, string? invalid, string? prompt) => JsonSerializer.Serialize(new
    {
        schemaVersion = QwenSceneImageEditPromptCompiler.SchemaVersion,
        status,
        sourceSummary = "Two women stand in a living room.",
        targets = Array.Empty<object>(),
        requestedChanges = Array.Empty<string>(),
        preserve = Array.Empty<string>(),
        clarificationQuestion = clarification,
        invalidReason = invalid,
        compiledPrompt = prompt
    });
}