namespace DreamGenClone.Web.Application.Import;

public interface ISessionImportService
{
    Task<(bool IsSuccess, string Message)> ImportJsonAsync(string json, CancellationToken cancellationToken = default);
}
