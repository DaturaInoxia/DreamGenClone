using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
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

    /// <summary>
    /// B-157 T2.2/T2.3 — the frame lives INSIDE the semantic snapshot, so a brief reloads carrying the framing it was
    /// compiled with, and the store's immutability rule is what stops a later staging-profile edit from rewriting how
    /// a past render was framed.
    /// </summary>
    [Fact]
    public async Task RoundTrip_PreservesTheSnapshottedBlocking()
    {
        var path = Path.Combine(Path.GetTempPath(), $"compiled-media-blocking-{Guid.NewGuid():N}.db");
        var repository = new CompiledMediaBriefRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={path}"
        }));

        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        const string Snapshot = """
            {"moment":{},"frozenState":{},"typedReferences":[],"blocking":{"profileVersion":"2","subjects":[{"subjectKey":"p0","position":{"horizontal":"Center","depth":"Foreground"},"roleText":"the person in the CENTER in the foreground"}],"camera":{"angle":"eye level","shotSize":"medium shot","side":null},"layering":["p0"],"rationale":"A wide two-shot."}}
            """;
        var brief = CreateBrief(now) with { SemanticInputSnapshotJson = Snapshot };

        await repository.CreateAsync(brief);
        var loaded = await repository.GetAsync(brief.Id);

        var blocking = SceneBlockingJson.TryRead(loaded!.SemanticInputSnapshotJson);
        Assert.NotNull(blocking);
        Assert.Equal("2", blocking!.ProfileVersion);
        Assert.Equal(
            new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground),
            Assert.Single(blocking.Subjects).Position);
        Assert.Equal("medium shot", blocking.Camera.ShotSize);

        // Immutability is the mechanism that makes the framing reproducible: a later write cannot replace the
        // snapshot, so editing the staging profile cannot reach back into a brief that was already compiled.
        var changed = brief with { SemanticInputSnapshotJson = """{"blocking":null}""" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateAsync(changed));
        Assert.Equal(Snapshot, (await repository.GetAsync(brief.Id))!.SemanticInputSnapshotJson);
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