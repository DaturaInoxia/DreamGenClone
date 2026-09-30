using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Test double for <see cref="IImageCompilerProfileResolver"/>: returns a valid profile carrying NO negative, which
/// is what every checkpoint declares except the cited Pony rows (B-135 D10). A test that cares about a negative must
/// use the real <c>ImageCompilerProfileRepository</c> against a temp database — that is where the rule lives.
/// </summary>
internal sealed class TestImageCompilerProfileResolver : IImageCompilerProfileResolver
{
    public Task<ImageCompilerProfile> ResolveAsync(ResolvedImageModel model, CancellationToken cancellationToken = default)
        => Task.FromResult(Profile(model.ModelIdentifier, model.SceneImageModelFamily, model.PromptDialect));

    public Task<ImageCompilerProfile> ResolveAsync(
        string checkpointIdentifier,
        string context = "test",
        CancellationToken cancellationToken = default)
        => Task.FromResult(Profile(checkpointIdentifier, SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage));

    private static ImageCompilerProfile Profile(
        string checkpoint,
        SceneImageModelFamily family,
        SceneImagePromptDialect dialect) => new()
        {
            Id = $"test-{checkpoint}",
            CheckpointIdentifier = checkpoint,
            DisplayName = "Test checkpoint",
            Family = family,
            PromptDialect = dialect,
            MinChars = 1,
            MaxChars = 100_000,
            MaxTokens = 10_000,
            PoseInText = ImagePoseInText.Full,
            Negative = string.Empty,
            SettingsEnvelopeJson = "{}",
            ResearchSource = "test double",
        };
}
