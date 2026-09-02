# Phase 2 Data Model - Character Identity

## Entities

### `CharacterImageIdentityPack`

| Field | Type | Rule |
|---|---|---|
| `Id` | string | Primary key. |
| `CharacterProfileId` | string | Required owner. |
| `Version` | int | Positive and unique per character. |
| `Status` | enum | `Draft`, `Approved`, `Superseded`. |
| `DescriptorSnapshotJson` | string | Stable visual descriptor at approval. |
| `CanonicalFaceAssetId` | string? | Required for approval. |
| `SupersedesId` | string? | Previous pack version. |
| `CreatedUtc`, `ApprovedUtc` | timestamp | Approval time required when approved. |

### `SceneImageReferenceAsset`

Fields: `Id`, `IdentityPackId`, `AssetKind` (`Face`, `FullBody`, `Wardrobe`),
`FaceView` (`Front`, `ThreeQuarterLeft`, `ThreeQuarterRight`, `ProfileLeft`, `ProfileRight`;
required for Face assets, null for other kinds),
`FileRelativePath`, `MediaType`, `Width`, `Height`, `ByteLength`, `Sha256`, `SourceLabel`,
`ConsentState` (`Unknown`, `Confirmed`, `NotApplicable`), `IsApproved`, `CreatedUtc`.

`Unknown` consent cannot be approved. A referenced file is immutable; replacement creates a new
asset. Paths use the existing scene-image storage safety rules. A pack may hold multiple classified
face references. A compiler may use only the exact ordered reference layout permitted by its
qualified capability cell; it never guesses a reference from target angle.

### `CharacterBodyProfileVersion` and `CharacterWardrobeLookVersion`

Immutable-on-approval aggregates owned by `CharacterProfileId`. Body versions reference approved
full-body/silhouette/proportion assets. Wardrobe versions reference approved garment/detail/color
assets and structured coverage facts. Attempts bind exact versions; supersession never rewrites
lineage.

### `MediaCapabilityProfile`

Fields include `Id`, `ProviderKey`, `ModelId`, `ModelVersion`, `Operation`, `CompilerId`,
`CompilerVersion`, `WorkflowRevision`, `NodeRevision`, `ArtifactManifestJson`,
`SettingsSchemaJson`, `ReferenceLayoutJson`, `ControlLayoutJson`, `ContentPolicyKey`, `Enabled`,
and timestamps. `MediaCapabilityCell` records actor count, angles, crop, pose/composition class,
operation, reference/control tuple, qualification state, evidence run, and gate result.

No field has a runtime default. Resolver validation supplies range rules but never substitutes a
value.

### `ProductionIntentSnapshot` and `CompiledMediaRequest`

The intent snapshot stores resolved B-100 IDs/versions plus typed character, composition, camera,
style, preservation/change, and policy facts. The compiled record stores exact compiler/profile,
canonical provider request JSON, ordered reference bindings, content hash, and validation result.
Neither stores secrets.

### `ProductionWorkload` and `ProductionWorkloadItem`

The workload is the durable user submission aggregate: session, status, goal, policy, cost/readiness
snapshot, creation/submission timestamps, and item counts. Items bind one intent, selected profile,
variation count, dependency/group key, state, and current-attempt pointer. Items exist before any
provider call.

### `ProductionAttempt` and `ProductionDerivative`

Attempts are append-only executions with attempt number/type, exact request snapshot, provider ID,
state, timing/cost, output/checksum, errors, review result, and supersession relation. A derivative is
an immutable approved asset linked to exactly one successful attempt and all source lineage.

### `SceneIdentityAssignment`

| Field | Purpose |
|---|---|
| `Id` | Stable assignment ID. |
| `ImageRecordId` or `RenderAttemptId` | Controlled output owner. |
| `ActorKey` | Stable actor identity in the beat/plan. |
| `IdentityPackId` | Exact approved version. |
| `RegionAssetId` | Exact mask/region file. |
| `ConditioningProfileId` | Exact resolved profile. |
| `StrengthSnapshotJson` | Immutable submitted values. |

### `SceneIdentityEvaluationCase` and `SceneIdentityEvaluationResult`

Cases persist matrix coordinates: character pair, pose key, view key, seed, prompt/control hashes,
and expected constraints. Results reference an output and store each scored dimension as
`Pass`, `Fail`, or `NotScored`, plus notes and reviewer timestamp.

### `CharacterLoraArtifact`

Created only when the LoRA decision is `Required`: `Id`, `IdentityPackId`, `CheckpointFamily`,
`TriggerToken`, `FileRelativePath`, `Sha256`, `TrainingManifestJson`, `DefaultStrength`, `Status`,
and timestamps. `DefaultStrength` is configuration metadata selected by the user, not a hardcoded
runtime fallback.

### `CharacterIdentityDecision`

Fields: `Id`, `IdentityPackId`, `EvaluationRunId`, `Decision` (`NotRequired`, `Required`,
`Deferred`), `Rationale`, `CreatedUtc`.

## Relationships

```mermaid
erDiagram
    CharacterImageIdentityPack ||--o{ SceneImageReferenceAsset : contains
    CharacterImageIdentityPack ||--o{ SceneIdentityAssignment : binds
    MediaCapabilityProfile ||--o{ MediaCapabilityCell : qualifies
    MediaCapabilityProfile ||--o{ SceneIdentityAssignment : configures
    ProductionWorkload ||--o{ ProductionWorkloadItem : contains
    ProductionWorkloadItem ||--o{ ProductionAttempt : executes
    ProductionAttempt ||--o| ProductionDerivative : approves
    SceneIdentityEvaluationCase ||--o{ SceneIdentityEvaluationResult : produces
    CharacterImageIdentityPack ||--o| CharacterLoraArtifact : may_train
    CharacterImageIdentityPack ||--o{ CharacterIdentityDecision : evaluated_by
```

## State Rules

- Draft packs may change metadata and membership.
- Approval freezes the version and requires one approved canonical face.
- Superseding creates a new draft; old records remain readable.
- Evaluation cases become immutable once a run starts.
- Workloads move `Draft -> Ready -> Submitted -> Running -> Reviewable -> Completed|Failed|Cancelled`.
- Items move `Draft -> Ready -> Queued -> Submitted -> Running -> Reviewable -> Approved|Rejected|Failed|Cancelled`.
- Attempts move `Created -> Submitted -> Running -> Succeeded|Failed|Cancelled|Expired` and never
    transition backward. Review/approval is separate from transport success.
- Capability cells move `Draft -> Proving -> Qualified|Rejected|Retired`; only `Qualified` dispatches.

## Clean-Baseline Strategy

Create the production tables/schema and stamp newly created sessions with the required production
schema generation. Do not backfill, synthesize, dual-write, or adapt prior session/image rows.
Opening an older session for production fails with create-new-session guidance. Historical proof
fixtures remain readable outside the runtime compatibility contract.
