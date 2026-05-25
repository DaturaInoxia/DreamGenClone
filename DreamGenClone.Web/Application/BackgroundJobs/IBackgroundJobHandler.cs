namespace DreamGenClone.Web.Application.BackgroundJobs;

public interface IBackgroundJobHandler
{
    string JobType { get; }

    Task HandleAsync(BackgroundJobEnvelope job, CancellationToken cancellationToken);
}
