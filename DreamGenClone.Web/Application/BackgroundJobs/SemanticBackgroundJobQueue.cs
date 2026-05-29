using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.BackgroundJobs;

public sealed class SemanticBackgroundJobQueue : ISemanticBackgroundJobQueue
{
    private readonly Channel<BackgroundJobEnvelope> _channel = Channel.CreateUnbounded<BackgroundJobEnvelope>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<string, string> _activeDedupeKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _processingJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SemanticBackgroundJobQueue> _logger;

    public SemanticBackgroundJobQueue(ILogger<SemanticBackgroundJobQueue> logger)
    {
        _logger = logger;
    }

    public bool Enqueue(string jobType, string payloadJson, string? dedupeKey = null)
    {
        if (string.IsNullOrWhiteSpace(jobType))
        {
            throw new InvalidOperationException("Background job type is required.");
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new InvalidOperationException("Background job payload is required.");
        }

        var normalizedJobType = jobType.Trim();
        var normalizedDedupeKey = NormalizeDedupeKey(normalizedJobType, dedupeKey);

        if (normalizedDedupeKey is not null && !_activeDedupeKeys.TryAdd(normalizedDedupeKey, normalizedDedupeKey))
        {
            _logger.LogDebug("Skipped duplicate semantic job enqueue: {JobType} {DedupeKey}", normalizedJobType, normalizedDedupeKey);
            return false;
        }

        var envelope = new BackgroundJobEnvelope
        {
            JobType = normalizedJobType,
            PayloadJson = payloadJson,
            DedupeKey = normalizedDedupeKey
        };

        _channel.Writer.TryWrite(envelope);
        return true;
    }

    public ValueTask<BackgroundJobEnvelope> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }

    public void MarkProcessing(string jobId)
    {
        _processingJobs[jobId] = jobId;
    }

    public void MarkCompleted(string jobId)
    {
        if (_processingJobs.TryRemove(jobId, out _))
        {
            // no-op
        }
    }

    public void MarkFailed(string jobId, string errorMessage)
    {
        _processingJobs.TryRemove(jobId, out _);
        _logger.LogWarning("Semantic background job {JobId} failed: {ErrorMessage}", jobId, errorMessage);
    }

    public void ReleaseDedupeKey(string? dedupeKey)
    {
        if (!string.IsNullOrWhiteSpace(dedupeKey))
        {
            _activeDedupeKeys.TryRemove(dedupeKey, out _);
        }
    }

    private static string? NormalizeDedupeKey(string jobType, string? dedupeKey)
    {
        if (string.IsNullOrWhiteSpace(dedupeKey))
        {
            return null;
        }

        return $"{jobType}:{dedupeKey.Trim()}";
    }
}
