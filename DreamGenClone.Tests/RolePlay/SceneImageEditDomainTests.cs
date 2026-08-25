using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageEditDomainTests
{
    [Fact]
    public void CompilationState_DefaultsToUnknownAndCannotAppearExecutable()
    {
        var attempt = new SceneImageEditCompilationAttempt();
        var result = new SceneImageEditCompilationResult();
        var revision = new SceneImageEditPromptRevision();

        Assert.Equal(SceneImageEditCompilationAttemptStatus.Unknown, attempt.Status);
        Assert.Equal(SceneImageEditCompilationResultStatus.Unknown, result.Status);
        Assert.Equal(SceneImageEditPromptRevisionKind.Unknown, revision.RevisionKind);
        Assert.Null(result.CompiledPrompt);
    }

    [Fact]
    public void Provider_HasNoImplicitLifecycleConfiguration()
    {
        var provider = new Provider();

        Assert.Null(provider.LifecycleStrategyIdentifier);
        Assert.Null(provider.ReadinessPath);
        Assert.Null(provider.TransitionTimeoutSeconds);
        Assert.Null(provider.TransitionMarginSeconds);
        Assert.Null(provider.MaximumActiveRequests);
        Assert.Null(provider.QueueCapacity);
    }

    [Fact]
    public void RegisteredModel_HasNoImplicitMultimodalLimits()
    {
        var model = new RegisteredModel();

        Assert.False(model.SupportsImageInput);
        Assert.Null(model.MaximumInputImages);
        Assert.Null(model.MaximumInputImageBytes);
        Assert.Null(model.MaximumInputImagePixels);
        Assert.Null(model.AcceptedInputMediaTypes);
    }

    [Fact]
    public void SceneImageRecord_EditProvenanceIsAdditive()
    {
        var record = new SceneImageRecord
        {
            EditSessionId = "edit-session",
            EditCompilationAttemptId = "attempt",
            EditPromptRevisionId = "revision",
            EditIntentSnapshot = "change the shirt to red",
            EditCompilerProvenanceJson = "{\"schemaVersion\":\"1\"}"
        };

        Assert.Equal("edit-session", record.EditSessionId);
        Assert.Equal("attempt", record.EditCompilationAttemptId);
        Assert.Equal("revision", record.EditPromptRevisionId);
        Assert.Equal("change the shirt to red", record.EditIntentSnapshot);
        Assert.Equal("{\"schemaVersion\":\"1\"}", record.EditCompilerProvenanceJson);
    }

    [Fact]
    public void AppFunctions_KeepCompilerAndValidatorAssignmentsDistinct()
    {
        Assert.NotEqual(
            AppFunction.RolePlaySceneImageEditPromptCompiler,
            AppFunction.RolePlaySceneImageValidator);
    }
}