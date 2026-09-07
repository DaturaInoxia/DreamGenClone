using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Application.RolePlay;

public sealed record ProducedImageQuery
{
    public ProducedImageKind? Kind { get; init; }
    public ProducedImageStatus? Status { get; init; }
    public ProducedImageReferenceKind? ReferenceKind { get; init; }
    public string? BatchId { get; init; }
    public string? TargetRef { get; init; }
    public string? ModelId { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 60;
}

public sealed record ProducedImagePage
{
    public IReadOnlyList<ProducedImage> Items { get; init; } = [];
    public int TotalCount { get; init; }
}