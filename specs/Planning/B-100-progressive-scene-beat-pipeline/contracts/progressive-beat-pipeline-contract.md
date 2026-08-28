# Progressive Beat Pipeline Contract

## Service Surface

Illustrative signatures; exact namespaces follow existing scene-image ownership boundaries.

```csharp
public interface ISceneBeatPipelineService
{
    Task<SceneBeatCatalogue> EnqueueCatalogueAsync(
        GenerateSceneBeatCatalogueRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneBeatCatalogue?> GetCurrentCatalogueAsync(
        string sessionId,
        string turnId,
        CancellationToken cancellationToken = default);

    Task<SceneBeatEnrichment> EnqueueEnrichmentAsync(
        EnrichSceneBeatRequest request,
        CancellationToken cancellationToken = default);

    Task<SceneBeatEnrichment?> GetCurrentEnrichmentAsync(
        string catalogueId,
        string beatId,
        CancellationToken cancellationToken = default);

    Task CancelCatalogueAsync(
        string catalogueId,
        CancellationToken cancellationToken = default);
}
```

Requests contain stable IDs only. The service resolves and snapshots authoritative state before accepting a durable job.

## Catalogue Output Contract

The catalogue response is intentionally compact:

```json
{
  "schemaVersion": 1,
  "beats": [
    {
      "beatId": "b1",
      "order": 1,
      "label": "Arrival at the doorway",
      "frozenMoment": "Becky pauses inside the doorway as Dean looks up.",
      "primaryLocation": "entry hall",
      "participants": [
        { "name": "Becky", "involvement": "active" },
        { "name": "Dean", "involvement": "observer" }
      ],
      "evidenceKeys": ["n0", "c1"]
    }
  ]
}
```

Catalogue output explicitly excludes:

- profile IDs;
- full clothing descriptions;
- precise positions and sightlines;
- visible-character matrices;
- detailed environment and lighting;
- image-family tags or prompts;
- provider workflow settings.

Validation rules:

- 1 to configured maximum catalogue entries;
- unique positive order and unique beat ID;
- concise bounded strings;
- at least one active participant;
- known participant names only;
- known evidence keys only;
- Narrative evidence key required for every entry;
- one atomic primary location.

Application code resolves evidence keys to authoritative interaction IDs. Unknown keys fail the attempt; they are never dropped or guessed.

## Enrichment Output Contract

Enrichment receives one immutable catalogue-entry snapshot plus only the authoritative evidence needed for that entry. It returns the detailed neutral visual contract:

```json
{
  "schemaVersion": 1,
  "catalogueBeatId": "b1",
  "visualDescription": "A complete description of one frozen moment.",
  "characters": [
    {
      "name": "Becky",
      "profileKey": "p0",
      "involvement": "active",
      "physicalLocation": "entry hall",
      "position": "just inside the doorway",
      "actionOrObservation": "pauses with one hand on the door",
      "sightline": "toward Dean",
      "visibleCharacterNames": ["Dean"],
      "clothing": "blue shirt"
    }
  ],
  "location": "entry hall",
  "timeOfDay": "evening",
  "lighting": "warm ceiling light",
  "environment": "narrow entry hall with the open door behind Becky",
  "mood": "expectant"
}
```

Profile keys are resolved to authoritative profile IDs by application code. Enrichment remains independent of Pony, SDXL, FLUX, Qwen, ComfyUI, or any provider prompt dialect.

## Structured Output Contract

- Both stages require a registered analyzer whose selected provider/model path supports strict JSON Schema.
- The request transport shall send the exact versioned schema, not only prose describing JSON.
- Model capability validation happens before durable job acceptance.
- Schema parsing uses structured APIs.
- No JSON repair, control-character sanitizer, inferred missing fields, alternate root names, or reasoning-as-content substitution is allowed.
- Schema-valid but semantically invalid output fails with a stable validation code.

## Model Resolution Contract

- Source: `AppFunction.RolePlaySceneBeatAnalyzer` only.
- A roleplay session `SessionModelId` is not an analyzer override.
- Missing function default, disabled model/provider, unsupported structured output, incompatible token limits, or unspecified required thinking mode fails explicitly.
- No model fallback or alternate provider path is allowed.
- The resolved model and execution settings are snapshotted before the job is accepted.

## Job Payloads

```csharp
public sealed record SceneBeatCatalogueJobPayload(
    string CatalogueId,
    string AttemptId);

public sealed record SceneBeatEnrichmentJobPayload(
    string EnrichmentId,
    string AttemptId);
```

Handlers reload only the immutable snapshot and current ownership metadata. Payloads do not embed prompt text, session blobs, model secrets, or response schemas.

## Concurrency Contract

Catalogue completion uses compare-and-set semantics equivalent to:

```sql
UPDATE SceneBeatCatalogues
SET Status = 'Complete', CompletedUtc = $now
WHERE Id = $catalogueId
  AND CurrentAttemptId = $attemptId
  AND Status IN ('Pending', 'Processing');
```

The update must affect exactly one row. Zero rows means the attempt is stale, cancelled, or already terminal. The attempt transitions to `Superseded` or observes idempotent completion; it must not upsert or replace the current catalogue.

The same rule applies to enrichment.

Dedupe keys:

- Catalogue attempt: `scene-beat-catalogue:{catalogueId}:{attemptId}`.
- Current enrichment request: `scene-beat-enrichment:{catalogueId}:{beatId}:{revision}`.

## Durable Queue Contract

- Persist job before returning success to the UI.
- Claim jobs transactionally with a worker ID and expiring lease.
- Recover expired Processing leases on startup.
- Separate lanes prevent long text analysis from blocking image rendering.
- Lane concurrency is required persisted configuration.
- Cancellation is a persisted state transition checked before and after model execution.

Retry categories:

| Category | Automatic retry |
|---|---|
| Timeout, connection reset, HTTP 429, eligible HTTP 5xx | Yes, within configured policy |
| Missing/invalid configuration | No |
| Unsupported capability | No |
| JSON Schema violation | No by default; explicit regeneration is required |
| Semantic validation failure | No by default |
| Superseded/cancelled attempt | No |
| Application shutdown cancellation | Recover through lease, not counted as a model failure unless request outcome is known |

Retry attempts reuse the immutable input and resolved model snapshot. They do not silently resolve a newer model.

## UI Contract

Catalogue states:

- `None`: Generate Beats command.
- `Pending`/`Processing`: progress, elapsed time, cancel command; Generate Again disabled.
- `Complete`: selectable catalogue; explicit Generate Again confirmation.
- `Failed`: stable error category, details, explicit Retry.
- `Superseded`/`Cancelled`: historical only.

Selection states:

- No enrichment: selecting queues enrichment.
- Pending enrichment: selected card remains visible with progress; prompt controls disabled.
- Complete enrichment: details and prompt controls enabled.
- Failed enrichment: catalogue remains usable; retry applies only to the selected beat.

## Image-Family Compiler Contract

```csharp
public interface ISceneImagePromptCompiler
{
    string FamilyKey { get; }
    string PromptDialect { get; }

    SceneImagePromptCompilation Build(
        SceneBeatVisualContract beat,
        SceneImageCompilationContext context);
}
```

Compiler resolution uses explicit registered-image-model metadata. Exactly one compiler must match. Zero or multiple matches fail explicitly. Catalogue/enrichment code never switches on an image family.

## Observability Contract

Each stage records:

- queued, started, completed/failed/superseded timestamps;
- queue wait and execution duration;
- prompt and schema versions;
- input/output character or token counts when available;
- resolved provider/model/settings;
- thinking mode and structured-output mode;
- finish reason;
- retry count and last transient failure;
- schema/semantic validation category;
- raw output and optional reasoning under retention policy.

Success metrics must be queryable separately for catalogue and enrichment.
