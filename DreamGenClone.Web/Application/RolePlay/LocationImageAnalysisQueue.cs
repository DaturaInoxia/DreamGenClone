using System.Text.Json;
using DreamGenClone.Application.Processing;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Web.Application.BackgroundJobs;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Asks for a location image to be read, and owns the shape of that ask (B-160).
/// </summary>
/// <remarks>
/// <para>
/// One place knows how the request is framed — its job type, its lane, its dedupe key and its retry budget — so the two
/// callers that need it (the operator's bind action in step 0, and the on-demand "read this location" action) cannot
/// drift into queueing two different jobs for the same work.
/// </para>
///
/// <para>
/// The lane is the one the sibling vision work uses (<c>PromptCompilation</c>, where the asset-edit description job
/// runs): it is the same model, the same client and the same concurrency profile, and the lane enum is persisted, so
/// reusing it is also the cheaper choice than appending a new one.
/// </para>
/// </remarks>
public interface ILocationImageAnalysisQueue
{
    /// <summary>
    /// Queues a reading of one location image.
    /// </summary>
    /// <param name="imageId">The image to read.</param>
    /// <param name="force">
    /// True for an operator asking to read it AGAIN: the stored reading is kept until the new one succeeds, but the
    /// job is not suppressed by the "already analysed for this checksum" rule.
    /// </param>
    /// <returns>True when the request was admitted to the queue.</returns>
    Task<bool> RequestAsync(string imageId, bool force = false, CancellationToken cancellationToken = default);
}

public sealed class LocationImageAnalysisQueue : ILocationImageAnalysisQueue
{
    private readonly IDurableBackgroundJobQueue _jobs;
    private readonly ILogger<LocationImageAnalysisQueue> _logger;

    public LocationImageAnalysisQueue(
        IDurableBackgroundJobQueue jobs,
        ILogger<LocationImageAnalysisQueue> logger)
    {
        _jobs = jobs;
        _logger = logger;
    }

    public async Task<bool> RequestAsync(
        string imageId,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageId))
        {
            throw new InvalidOperationException("A location image id is required to read a location image.");
        }

        var now = DateTime.UtcNow;
        var id = Guid.NewGuid().ToString("N");

        var enqueued = await _jobs.TryEnqueueAsync(
            new DurableBackgroundJob
            {
                Id = id,
                JobType = BackgroundJobTypes.LocationImageAnalysis,
                Lane = DurableJobLane.PromptCompilation,
                PayloadJson = JsonSerializer.Serialize(
                    new LocationImageAnalysisJobHandler.LocationImageAnalysisJobPayload(imageId.Trim(), force)),
                // Dedupe by IMAGE, so two triggers at once (a bind and an impatient click) cost one reading. A forced
                // re-read has to be a different key or the queue would correctly call it a duplicate and drop it.
                DedupeKey = force
                    ? $"{BackgroundJobTypes.LocationImageAnalysis}:{imageId.Trim()}:{now.Ticks}"
                    : $"{BackgroundJobTypes.LocationImageAnalysis}:{imageId.Trim()}",
                MaxAttempts = 2,
                CreatedUtc = now,
                UpdatedUtc = now
            },
            cancellationToken);

        _logger.LogInformation(
            "Location image analysis requested: ImageId={ImageId}, Force={Force}, Enqueued={Enqueued}",
            imageId, force, enqueued);

        return enqueued;
    }
}
