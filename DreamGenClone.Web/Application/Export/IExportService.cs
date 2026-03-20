namespace DreamGenClone.Web.Application.Export;

public interface IExportService
{
    Task<string?> ExportJsonAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<string?> ExportMarkdownAsync(string sessionId, CancellationToken cancellationToken = default);
}
