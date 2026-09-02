using System.Globalization;
using System.Text.Json;
using DreamGenClone.Application.Processing;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.Processing;

public sealed class DurableBackgroundJobRepository : IDurableBackgroundJobRepository
{
    private readonly string _connectionString;

    public DurableBackgroundJobRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task<bool> TryEnqueueAsync(
        DurableBackgroundJob job,
        CancellationToken cancellationToken = default)
    {
        ValidateNewJob(job);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO DurableBackgroundJobs (
                Id, JobType, Lane, PayloadJson, DedupeKey, Status, AttemptCount, MaxAttempts,
                NextAttemptUtc, LeaseOwner, LeaseExpiresUtc, ErrorCode, ErrorMessage,
                CreatedUtc, UpdatedUtc, CompletedUtc)
            VALUES (
                $id, $jobType, $lane, $payloadJson, $dedupeKey, 'Queued', 0, $maxAttempts,
                NULL, NULL, NULL, NULL, NULL, $createdUtc, $createdUtc, NULL);
            """;
        command.Parameters.AddWithValue("$id", job.Id.Trim());
        command.Parameters.AddWithValue("$jobType", job.JobType.Trim());
        command.Parameters.AddWithValue("$lane", job.Lane.ToString());
        command.Parameters.AddWithValue("$payloadJson", job.PayloadJson);
        command.Parameters.AddWithValue("$dedupeKey", job.DedupeKey.Trim());
        command.Parameters.AddWithValue("$maxAttempts", job.MaxAttempts);
        command.Parameters.AddWithValue("$createdUtc", FormatUtc(job.CreatedUtc));
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async Task<DurableBackgroundJob?> GetAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        Require(jobId, "Job id");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = CreateSelect(connection);
        command.CommandText += " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", jobId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<bool> HasActiveJobsAsync(
        DurableJobLane lane,
        CancellationToken cancellationToken = default)
    {
        RequireDefined(lane, "Job lane");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM DurableBackgroundJobs
                WHERE Lane = $lane AND Status IN ('Queued', 'Processing', 'RetryScheduled'));
            """;
        command.Parameters.AddWithValue("$lane", lane.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<DurableBackgroundJob?> TryClaimNextAsync(
        DurableJobLane lane,
        string leaseOwner,
        DateTime claimedUtc,
        DateTime leaseExpiresUtc,
        CancellationToken cancellationToken = default)
    {
        RequireDefined(lane, "Job lane");
        Require(leaseOwner, "Lease owner");
        RequireUtc(claimedUtc, "Claimed UTC");
        RequireUtc(leaseExpiresUtc, "Lease expiry UTC");
        if (leaseExpiresUtc <= claimedUtc)
            throw new InvalidOperationException("Lease expiry must be after the claim time.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DurableBackgroundJobs
            SET Status = 'Processing', AttemptCount = AttemptCount + 1,
                LeaseOwner = $leaseOwner, LeaseExpiresUtc = $leaseExpiresUtc,
                NextAttemptUtc = NULL, ErrorCode = NULL, ErrorMessage = NULL, UpdatedUtc = $claimedUtc
            WHERE Id = (
                SELECT Id FROM DurableBackgroundJobs
                WHERE Lane = $lane
                  AND Status IN ('Queued', 'RetryScheduled')
                  AND AttemptCount < MaxAttempts
                  AND (NextAttemptUtc IS NULL OR NextAttemptUtc <= $claimedUtc)
                ORDER BY CreatedUtc, Id
                LIMIT 1
            )
            RETURNING Id, JobType, Lane, PayloadJson, DedupeKey, Status, AttemptCount, MaxAttempts,
                      NextAttemptUtc, LeaseOwner, LeaseExpiresUtc, ErrorCode, ErrorMessage,
                      CreatedUtc, UpdatedUtc, CompletedUtc;
            """;
        command.Parameters.AddWithValue("$lane", lane.ToString());
        command.Parameters.AddWithValue("$leaseOwner", leaseOwner.Trim());
        command.Parameters.AddWithValue("$claimedUtc", FormatUtc(claimedUtc));
        command.Parameters.AddWithValue("$leaseExpiresUtc", FormatUtc(leaseExpiresUtc));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public Task<bool> TryRenewLeaseAsync(
        string jobId,
        string leaseOwner,
        DateTime renewedUtc,
        DateTime leaseExpiresUtc,
        CancellationToken cancellationToken = default)
    {
        RequireUtc(leaseExpiresUtc, "Lease expiry UTC");
        if (leaseExpiresUtc <= renewedUtc)
            throw new InvalidOperationException("Lease expiry must be after the renewal time.");
        return ExecuteOwnedTransitionAsync(
            jobId,
            leaseOwner,
            renewedUtc,
            "LeaseExpiresUtc = $leaseExpiresUtc, UpdatedUtc = $transitionUtc",
            command => command.Parameters.AddWithValue("$leaseExpiresUtc", FormatUtc(leaseExpiresUtc)),
            cancellationToken);
    }

    public Task<bool> TryScheduleRetryAsync(
        string jobId,
        string leaseOwner,
        string errorCode,
        string errorMessage,
        DateTime scheduledUtc,
        DateTime nextAttemptUtc,
        CancellationToken cancellationToken = default)
    {
        Require(errorCode, "Error code");
        Require(errorMessage, "Error message");
        RequireUtc(nextAttemptUtc, "Next attempt UTC");
        if (nextAttemptUtc <= scheduledUtc)
            throw new InvalidOperationException("Next attempt time must be after the retry scheduling time.");
        return ExecuteOwnedTransitionAsync(
            jobId,
            leaseOwner,
            scheduledUtc,
            "Status = 'RetryScheduled', NextAttemptUtc = $nextAttemptUtc, LeaseOwner = NULL, LeaseExpiresUtc = NULL, ErrorCode = $errorCode, ErrorMessage = $errorMessage, UpdatedUtc = $transitionUtc",
            command =>
            {
                command.Parameters.AddWithValue("$nextAttemptUtc", FormatUtc(nextAttemptUtc));
                command.Parameters.AddWithValue("$errorCode", errorCode.Trim());
                command.Parameters.AddWithValue("$errorMessage", errorMessage.Trim());
            },
            cancellationToken,
            " AND AttemptCount < MaxAttempts");
    }

    public Task<bool> TryCompleteAsync(
        string jobId,
        string leaseOwner,
        DateTime completedUtc,
        CancellationToken cancellationToken = default)
        => ExecuteOwnedTransitionAsync(
            jobId,
            leaseOwner,
            completedUtc,
            "Status = 'Complete', LeaseOwner = NULL, LeaseExpiresUtc = NULL, ErrorCode = NULL, ErrorMessage = NULL, UpdatedUtc = $transitionUtc, CompletedUtc = $transitionUtc",
            null,
            cancellationToken);

    public Task<bool> TryFailAsync(
        string jobId,
        string leaseOwner,
        string errorCode,
        string errorMessage,
        DateTime failedUtc,
        CancellationToken cancellationToken = default)
    {
        Require(errorCode, "Error code");
        Require(errorMessage, "Error message");
        return ExecuteOwnedTransitionAsync(
            jobId,
            leaseOwner,
            failedUtc,
            "Status = 'Failed', LeaseOwner = NULL, LeaseExpiresUtc = NULL, ErrorCode = $errorCode, ErrorMessage = $errorMessage, UpdatedUtc = $transitionUtc, CompletedUtc = $transitionUtc",
            command =>
            {
                command.Parameters.AddWithValue("$errorCode", errorCode.Trim());
                command.Parameters.AddWithValue("$errorMessage", errorMessage.Trim());
            },
            cancellationToken);
    }

    public async Task<bool> TryCancelAsync(
        string jobId,
        DateTime cancelledUtc,
        CancellationToken cancellationToken = default)
    {
        Require(jobId, "Job id");
        RequireUtc(cancelledUtc, "Cancelled UTC");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DurableBackgroundJobs
            SET Status = 'Cancelled', LeaseOwner = NULL, LeaseExpiresUtc = NULL,
                NextAttemptUtc = NULL, UpdatedUtc = $cancelledUtc, CompletedUtc = $cancelledUtc
            WHERE Id = $id AND Status IN ('Queued', 'Processing', 'RetryScheduled');
            """;
        command.Parameters.AddWithValue("$id", jobId.Trim());
        command.Parameters.AddWithValue("$cancelledUtc", FormatUtc(cancelledUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<int> RecoverExpiredLeasesAsync(
        DateTime recoveredUtc,
        CancellationToken cancellationToken = default)
    {
        RequireUtc(recoveredUtc, "Recovered UTC");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DurableBackgroundJobs
            SET Status = CASE WHEN AttemptCount < MaxAttempts THEN 'Queued' ELSE 'Failed' END,
                LeaseOwner = NULL, LeaseExpiresUtc = NULL, NextAttemptUtc = NULL,
                ErrorCode = CASE WHEN AttemptCount < MaxAttempts THEN NULL ELSE 'lease_expired_attempts_exhausted' END,
                ErrorMessage = CASE WHEN AttemptCount < MaxAttempts THEN NULL ELSE 'The processing lease expired after the configured maximum attempt count.' END,
                UpdatedUtc = $recoveredUtc,
                CompletedUtc = CASE WHEN AttemptCount < MaxAttempts THEN NULL ELSE $recoveredUtc END
            WHERE Status = 'Processing' AND LeaseExpiresUtc <= $recoveredUtc;
            """;
        command.Parameters.AddWithValue("$recoveredUtc", FormatUtc(recoveredUtc));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> ExecuteOwnedTransitionAsync(
        string jobId,
        string leaseOwner,
        DateTime transitionUtc,
        string assignments,
        Action<SqliteCommand>? addParameters,
        CancellationToken cancellationToken,
        string additionalPredicate = "")
    {
        Require(jobId, "Job id");
        Require(leaseOwner, "Lease owner");
        RequireUtc(transitionUtc, "Transition UTC");
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE DurableBackgroundJobs
            SET {assignments}
            WHERE Id = $id AND Status = 'Processing' AND LeaseOwner = $leaseOwner
              AND LeaseExpiresUtc > $transitionUtc{additionalPredicate};
            """;
        command.Parameters.AddWithValue("$id", jobId.Trim());
        command.Parameters.AddWithValue("$leaseOwner", leaseOwner.Trim());
        command.Parameters.AddWithValue("$transitionUtc", FormatUtc(transitionUtc));
        addParameters?.Invoke(command);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS DurableBackgroundJobs (
                Id TEXT PRIMARY KEY,
                JobType TEXT NOT NULL,
                Lane TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                DedupeKey TEXT NOT NULL,
                Status TEXT NOT NULL,
                AttemptCount INTEGER NOT NULL,
                MaxAttempts INTEGER NOT NULL,
                NextAttemptUtc TEXT NULL,
                LeaseOwner TEXT NULL,
                LeaseExpiresUtc TEXT NULL,
                ErrorCode TEXT NULL,
                ErrorMessage TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                CompletedUtc TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS UX_DurableBackgroundJobs_ActiveDedupe
                ON DurableBackgroundJobs (DedupeKey)
                WHERE Status IN ('Queued', 'Processing', 'RetryScheduled');
            CREATE INDEX IF NOT EXISTS IX_DurableBackgroundJobs_Claim
                ON DurableBackgroundJobs (Lane, Status, NextAttemptUtc, CreatedUtc);
            CREATE INDEX IF NOT EXISTS IX_DurableBackgroundJobs_Lease
                ON DurableBackgroundJobs (Status, LeaseExpiresUtc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand CreateSelect(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, JobType, Lane, PayloadJson, DedupeKey, Status, AttemptCount, MaxAttempts,
                   NextAttemptUtc, LeaseOwner, LeaseExpiresUtc, ErrorCode, ErrorMessage,
                   CreatedUtc, UpdatedUtc, CompletedUtc
            FROM DurableBackgroundJobs
            """;
        return command;
    }

    private static DurableBackgroundJob Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        JobType = reader.GetString(1),
        Lane = ParseEnum<DurableJobLane>(reader.GetString(2)),
        PayloadJson = reader.GetString(3),
        DedupeKey = reader.GetString(4),
        Status = ParseEnum<DurableBackgroundJobStatus>(reader.GetString(5)),
        AttemptCount = reader.GetInt32(6),
        MaxAttempts = reader.GetInt32(7),
        NextAttemptUtc = NullableUtc(reader, 8),
        LeaseOwner = NullableString(reader, 9),
        LeaseExpiresUtc = NullableUtc(reader, 10),
        ErrorCode = NullableString(reader, 11),
        ErrorMessage = NullableString(reader, 12),
        CreatedUtc = ParseUtc(reader.GetString(13)),
        UpdatedUtc = ParseUtc(reader.GetString(14)),
        CompletedUtc = NullableUtc(reader, 15)
    };

    private static void ValidateNewJob(DurableBackgroundJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Require(job.Id, "Job id");
        Require(job.JobType, "Job type");
        RequireDefined(job.Lane, "Job lane");
        Require(job.PayloadJson, "Payload JSON");
        Require(job.DedupeKey, "Dedupe key");
        RequireUtc(job.CreatedUtc, "Created UTC");
        if (job.Status != DurableBackgroundJobStatus.Queued || job.AttemptCount != 0)
            throw new InvalidOperationException("A new durable job must be Queued with zero attempts.");
        if (job.MaxAttempts < 1)
            throw new InvalidOperationException("A durable job requires a positive configured maximum attempt count.");
        if (job.NextAttemptUtc is not null || job.LeaseOwner is not null || job.LeaseExpiresUtc is not null
            || job.ErrorCode is not null || job.ErrorMessage is not null || job.CompletedUtc is not null)
            throw new InvalidOperationException("A new durable job cannot contain transition state.");
        try
        {
            using var document = JsonDocument.Parse(job.PayloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Durable job payload JSON must be an object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Durable job payload JSON is malformed.", ex);
        }
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} is required.");
    }

    private static void RequireDefined<T>(T value, string label) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new InvalidOperationException($"{label} is invalid.");
    }

    private static void RequireUtc(DateTime value, string label)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException($"{label} must have DateTimeKind.Utc.");
    }

    private static string FormatUtc(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static T ParseEnum<T>(string value) where T : struct, Enum
        => Enum.TryParse<T>(value, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"Stored value '{value}' is invalid for {typeof(T).Name}.");

    private static string? NullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime ParseUtc(string value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : throw new InvalidOperationException($"Stored UTC value '{value}' is invalid.");

    private static DateTime? NullableUtc(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : ParseUtc(reader.GetString(ordinal));
}