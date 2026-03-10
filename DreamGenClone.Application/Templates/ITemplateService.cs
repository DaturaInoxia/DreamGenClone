using DreamGenClone.Domain.Templates;

namespace DreamGenClone.Application.Templates;

public interface ITemplateService
{
    Task<IReadOnlyList<TemplateDefinition>> GetAllAsync(TemplateType? templateType = null, CancellationToken cancellationToken = default);

    Task<TemplateDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TemplateDefinition> SaveAsync(TemplateDefinition template, CancellationToken cancellationToken = default);

    Task UpdateImagePathAsync(Guid id, string imagePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
