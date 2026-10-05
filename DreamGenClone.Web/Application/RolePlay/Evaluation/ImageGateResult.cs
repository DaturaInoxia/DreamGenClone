using System.Text.Json;

namespace DreamGenClone.Web.Application.RolePlay.Evaluation;

/// <summary>
/// One native gate's measured verdict on a rendered image (B-135 P3). The name is the gate, <see cref="Pass"/> is
/// the verdict, and <see cref="Detail"/> carries the measured number. A gate records and shows — it never blocks,
/// retries, or changes the image (operator 2026-10-04: "no gate ever blocks").
/// </summary>
public sealed record ImageGateResult(
    string Name,
    bool Pass,
    string Detail);

/// <summary>The wire shape for <see cref="Domain.RolePlay.SceneAssetImage.GateResultsJson"/>.</summary>
public static class ImageGateResults
{
    public static string Serialize(IReadOnlyList<ImageGateResult> results) =>
        JsonSerializer.Serialize(results);

    public static IReadOnlyList<ImageGateResult> Read(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<ImageGateResult>>(json) ?? [];
}
