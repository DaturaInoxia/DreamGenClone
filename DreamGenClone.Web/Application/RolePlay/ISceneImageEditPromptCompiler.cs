using System.Text.Json;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record SceneImageEditCompilerContext(
    string RawIntent,
    IReadOnlyList<string> ClarificationHistory);

public sealed record SceneImageEditCompilerMessages(
    string SchemaVersion,
    string SystemPromptVersion,
    string SystemMessage,
    string UserMessage,
    string ResponseSchemaName,
    JsonElement ResponseSchema);

public interface ISceneImageEditPromptCompiler
{
    SceneImageEditCompilerMessages BuildMessages(SceneImageEditCompilerContext context);

    SceneImageEditCompilationResult Parse(string rawResponse, int imageWidth, int imageHeight);
}