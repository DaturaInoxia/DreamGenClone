using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatProductionSnapshotBuilderTests
{
    private readonly SceneBeatProductionSnapshotBuilder _builder = new();

    [Fact]
    public void Build_FreezesSelectedBeatAndOnlyItsResolvedEvidenceAndProfiles()
    {
        var (catalogue, entry) = CreateFixture();

        var snapshot = _builder.Build(catalogue, entry);

        Assert.Equal(SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal("catalogue-1", snapshot.CatalogueId);
        Assert.Equal(3, snapshot.CatalogueVersion);
        Assert.Equal("b1", snapshot.Beat.BeatId);
        Assert.Equal("entry hall", snapshot.Beat.PrimaryLocation);
        Assert.Equal(["n0", "c2"], snapshot.Beat.EvidenceKeys);
        Assert.Equal(["narrative-id", "becky-id"], snapshot.Evidence.Select(item => item.InteractionId));
        Assert.Equal(["Becky", "Dean"], snapshot.Profiles.Select(item => item.Name));
        Assert.Equal(["p0", "p1"], snapshot.Beat.Participants.Select(item => item.ProfileKey));
        Assert.DoesNotContain(snapshot.Evidence, item => item.InteractionId == "unrelated-id");

        catalogue.InputSnapshotJson = "mutated";
        entry.BeatSynopsis = "mutated";
        Assert.Equal("Becky enters and Dean notices her.", snapshot.Beat.Synopsis);
        Assert.Contains("Narrative synthesis", _builder.Serialize(snapshot), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsEvidenceOrParticipantOutsideImmutableCatalogueSnapshot()
    {
        var (catalogue, entry) = CreateFixture();
        entry.EvidenceInteractionIdsJson = "[\"missing\"]";
        var evidenceError = Assert.Throws<InvalidOperationException>(() => _builder.Build(catalogue, entry));
        Assert.Contains("missing from the immutable Turn snapshot", evidenceError.Message, StringComparison.Ordinal);

        (catalogue, entry) = CreateFixture();
        entry.ParticipantSummaryJson = "[{\"name\":\"Unknown\",\"involvement\":\"active\"}]";
        var profileError = Assert.Throws<InvalidOperationException>(() => _builder.Build(catalogue, entry));
        Assert.Contains("missing immutable profiles", profileError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsNonCurrentLineageAndIncompleteCatalogue()
    {
        var (catalogue, entry) = CreateFixture();
        catalogue.Status = SceneBeatCatalogueStatus.Processing;
        Assert.Throws<InvalidOperationException>(() => _builder.Build(catalogue, entry));

        (catalogue, entry) = CreateFixture();
        entry.CatalogueId = "other";
        Assert.Throws<InvalidOperationException>(() => _builder.Build(catalogue, entry));
    }

    private static (SceneBeatCatalogue Catalogue, SceneBeatCatalogueEntry Entry) CreateFixture()
    {
        var source = new SceneBeatCatalogueInputSnapshot(
            1,
            "session-1",
            "turn-1",
            7,
            "SubmitPrompt",
            new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 10, 0, 5, DateTimeKind.Utc),
            new string('A', 64),
            [
                new("n0", 2, "narrative-id", "Narrative", "System", "Narrative synthesis", new DateTime(2026, 9, 1, 10, 0, 3, DateTimeKind.Utc), new string('B', 64)),
                new("c1", 0, "unrelated-id", "Other", "User", "Unrelated", new DateTime(2026, 9, 1, 10, 0, 1, DateTimeKind.Utc), new string('C', 64)),
                new("c2", 1, "becky-id", "Becky", "Npc", "Becky enters", new DateTime(2026, 9, 1, 10, 0, 2, DateTimeKind.Utc), new string('D', 64))
            ],
            [
                new("p0", "character-becky", "Becky", "Wife", "Female", "", "", "", false, new string('E', 64)),
                new("p1", "character-dean", "Dean", "Husband", "Male", "", "", "", true, new string('F', 64)),
                new("p2", "character-other", "Other", "Observer", "Male", "", "", "", false, new string('1', 64))
            ],
            []);
        var entry = new SceneBeatCatalogueEntry
        {
            CatalogueId = "catalogue-1",
            BeatId = "b1",
            Order = 1,
            Label = "Arrival",
            BeatSynopsis = "Becky enters and Dean notices her.",
            PrimaryLocation = "entry hall",
            ParticipantSummaryJson = "[{\"name\":\"Becky\",\"involvement\":\"active\"},{\"name\":\"Dean\",\"involvement\":\"observer\"}]",
            EvidenceInteractionIdsJson = "[\"narrative-id\",\"becky-id\"]",
            ContentTagsJson = "[]"
        };
        var catalogue = new SceneBeatCatalogue
        {
            Id = "catalogue-1",
            SessionId = "session-1",
            TurnId = "turn-1",
            Version = 3,
            Status = SceneBeatCatalogueStatus.Complete,
            InputSnapshotJson = JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Entries = [entry]
        };
        return (catalogue, entry);
    }
}