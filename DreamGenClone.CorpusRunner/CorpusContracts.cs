using System.Text.Json.Serialization;

namespace DreamGenClone.CorpusRunner;

public sealed record CorpusManifest(string Version, IReadOnlyList<CorpusManifestEntry> Cases);

public sealed record CorpusManifestEntry(string Id, string File);

public sealed record FrozenCorpusCase(
    string Id,
    string Category,
    string Description,
    FrozenSession Session,
    FrozenTurn Turn,
    IReadOnlyList<FrozenCharacter> Characters,
    CorpusExpectations? Expectations,
    ExpectedPreflightRejection? ExpectedPreflightRejection);

public sealed record FrozenSession(
    string Id,
    string ScenarioId,
    string PersonaCharacterId,
    string PersonaName,
    string PersonaRole,
    string PersonaGender,
    string PersonaDescription,
    IReadOnlyList<FrozenInteraction> Interactions);

public sealed record FrozenInteraction(
    string Id,
    string ActorName,
    string InteractionType,
    string Content,
    DateTime CreatedUtc);

public sealed record FrozenTurn(
    string Id,
    int Index,
    string Kind,
    string TriggerSource,
    string InputInteractionId,
    IReadOnlyList<string> OutputInteractionIds,
    DateTime StartedUtc,
    DateTime CompletedUtc);

public sealed record FrozenCharacter(
    string Id,
    string Name,
    string Role,
    string Gender,
    string Description);

public sealed record CorpusExpectations(
    IReadOnlyList<ExpectedBeatBoundary> BeatBoundaries,
    int SelectedBeatOrdinal,
    ExpectedMoments Moments,
    IReadOnlyList<RequiredSourceFact> RequiredSourceFacts);

public sealed record ExpectedBeatBoundary(
    string Label,
    IReadOnlyList<string> EvidenceInteractionIds,
    string Match);

public sealed record ExpectedMoments(
    int Minimum,
    int Maximum,
    bool RecommendedRequired,
    IReadOnlyList<string> RequiredProductionRoles);

public sealed record RequiredSourceFact(
    string FactKey,
    string EvidenceInteractionId,
    string Description);

public sealed record ExpectedPreflightRejection(string Code, string Reason);

public sealed record LoadedCorpus(
    string Version,
    string ChecksumSha256,
    string ManifestPath,
    IReadOnlyList<FrozenCorpusCase> Cases);