using DreamGenClone.Domain.Processing;

namespace DreamGenClone.Application.Processing;

/// <summary>
/// The queue bounds ONE durable lane runs with (B-152, D-7). Every value comes from that lane's own configured
/// function default; nothing is shared between lanes by construction, because a video render occupies the single
/// GPU for an hour while a text analysis takes seconds.
/// </summary>
/// <param name="Lane">The lane these bounds apply to.</param>
/// <param name="MaxConcurrentJobs">Worker count for the lane.</param>
/// <param name="LeaseSeconds">Lease length; the executor renews at half of it.</param>
/// <param name="PollIntervalMilliseconds">Idle poll interval for the lane's workers.</param>
/// <param name="RetryDelaysSeconds">One delay per retry, in order.</param>
/// <param name="ProviderTimeoutSeconds">
/// The lane's provider timeout, used for the structured-text operation watchdog. Lanes without that watchdog
/// (image and video render) still carry it, and it is simply not consulted.
/// </param>
public sealed record DurableLaneSettings(
    DurableJobLane Lane,
    int MaxConcurrentJobs,
    int LeaseSeconds,
    int PollIntervalMilliseconds,
    IReadOnlyList<int> RetryDelaysSeconds,
    int ProviderTimeoutSeconds);
