using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class CompiledMediaBriefRepositoryTests
{
    [Fact]
    public async Task BriefAndDerivative_RoundTripRemainImmutableAndQueryableByCanonicalLineage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"compiled-media-{Guid.NewGuid():N}.db");
        var repository = new CompiledMediaBriefRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={path}"
        }));
        var derivatives = (IApprovedMediaDerivativeRepository)repository;
        try
        {
            var now = new DateTime(2026, 8, 31, 15, 0, 0, DateTimeKind.Utc);
            var brief = CreateBrief(now);
            await repository.CreateAsync(brief);

            var loaded = await repository.GetAsync(brief.Id);
            Assert.Equivalent(brief, loaded, strict: true);
            Assert.Equal(brief.Id, Assert.Single(await repository.ListByMomentEnrichmentAsync("enrichment-1")).Id);
            Assert.Equal(brief.Id, Assert.Single(await repository.ListByBeatProductionPlanAsync("plan-1")).Id);

            var changed = brief with { SemanticInputSnapshotJson = "{\"changed\":true}" };
            var duplicateError = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateAsync(changed));
            Assert.Contains("immutable", duplicateError.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(brief.SemanticInputSnapshotJson, (await repository.GetAsync(brief.Id))!.SemanticInputSnapshotJson);

            var alignment = new RealizedMediaAlignment(2.5m, 48000, null,
                [new("H", 0, 0.1m)], [new("Hello", 0, 0.8m)], "provider-request-1", ["dialogue-1"], now);
            var derivative = new ApprovedMediaDerivative("derivative-1", 1, MediaProductionKind.Speech,
                brief.Id, brief.TargetProfileVersion, ["dialogue-1"], "asset-1", "sha256-1", alignment, now, now);
            await derivatives.CreateAsync(derivative);

            var loadedDerivative = await derivatives.GetAsync(derivative.Id);
            Assert.Equivalent(derivative, loadedDerivative, strict: true);
            Assert.Equal(2.5m, loadedDerivative!.RealizedAlignment!.ActualDurationSeconds);
            Assert.Equal("provider-request-1", loadedDerivative.RealizedAlignment.ProviderRequestId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => derivatives.CreateAsync(derivative));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try
                {
                    if (File.Exists(path + suffix)) File.Delete(path + suffix);
                }
                catch
                {
                }
            }
        }
    }

    private static CompiledMediaBrief CreateBrief(DateTime now)
    {
        var coverage = JsonSerializer.Serialize(new RequiredIntentCoverageReport(
            [new("SpeechText", RequiredIntentCoverageStatus.Supported, "explicitly supported")]));
        return new CompiledMediaBrief(
            "brief-1", MediaProductionKind.Speech, "profile-1", "1", "canonical", "deterministic", "1",
            "canonical-request-v1",
            new("catalogue-1", "beat-1", "plan-1", 2, "set-1", 3, "moment-1", "enrichment-1", 4),
            ["plan-1", "set-1", "moment-1", "enrichment-1"],
            "{\"spokenText\":\"Hello\"}", "{\"contractVersion\":\"canonical-request-v1\"}", coverage,
            MediaCompilerStatus.Complete, null, null, now, now);
    }
}