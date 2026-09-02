# Phase 3 Data Model - Location and Multi-POV

## Versioned Aggregate Roots

### `LocationVisualProfile`

Fields: `Id`, `LocationKey`, `Name`, `Version`, `Status`, `WidthCm`, `LengthCm`, `HeightCm`,
`CoordinateConvention`, `Description`, `VisualStyleJson`, `LightingIntentJson`, `ExclusionsJson`,
`SupersedesId`, timestamps.

`LocationStateVersion` is a separately approved child/version that records time, weather, lighting,
temporary dressing, active entrances/openings, palette, references, and supersession. It cannot
rewrite structural landmarks in the owning profile.

Children:

- `LocationLandmark`: stable `LandmarkKey`, type, dimensions, transform, material/appearance JSON,
  permanence, and evidence.
- `LocationReferenceAsset`: same integrity/provenance contract as Phase 2 references.

### `SceneVisualPlan`

Fields: `Id`, `ScenarioId`, `SessionId`, `InteractionId`, `BeatAnalysisId`, `BeatKey`, `Version`,
`Status`, `LocationProfileId`, `LocationStateVersionId`, `MomentId`, `MomentVersion`,
`WorldStateJson`, `InvariantSetJson`, `EvidenceSnapshotJson`, `SupersedesId`, timestamps.

Normalized child records are preferred for fields queried or referenced independently:

- `SceneVisualActor`: `ActorKey`, character ID, identity-pack ID, transform, body dimensions,
  skeleton/joints JSON, wardrobe JSON, appearance overrides, evidence.
- `SceneVisualObject`: `ObjectKey`, type, location/portable ownership, dimensions, transform,
  appearance JSON, evidence.
- `SceneVisualRelationship`: `RelationshipKey`, subject key, predicate enum, object key or anchor,
  parameters JSON, importance (`Required`, `Preferred`), validation mode, evidence.

### `SceneShotPlan`

Fields: `Id`, `SceneVisualPlanId`, `ShotKey`, `Version`, `Status`, `CameraTransformJson`,
`ProjectionType`, `VerticalFovDegrees`, `NearCm`, `FarCm`, `AspectWidth`, `AspectHeight`,
`ShotType`, `Purpose`, `PovActorKey`, `SubjectPriorityJson`, `CropIntent`, `FocusIntentJson`,
`VisibleKeysJson`, `OccludedKeysJson`, `ScreenDirectionJson`, `MovementIntentJson`,
`PlacementIntentJson`, `SupersedesId`, timestamps.

### `SceneControlAsset`

Fields: `Id`, `SceneShotPlanId`, `ControlKind` (`Depth`, `Pose`, `ActorRegion`, `SemanticMask`,
`Preview`), optional `SemanticKey`, file integrity fields, `CompilerName`, `CompilerVersion`,
`InputHash`, timestamps.

### `SceneControlManifest`

Fields: `Id`, `SceneShotPlanId`, `ManifestVersion`, `Status`, `CompilerVersion`, `InputHash`,
`ManifestJson`, `CreatedUtc`. The manifest lists exact control IDs, dimensions, preprocessing,
configured model artifacts/weights, and compatibility data.

### `ShotFamily` and `ShotFamilyInvariant`

`ShotFamily` binds one approved visual-plan version to ordered required/optional shot keys and one
production goal. Invariants use typed subject/predicate/object or property/value facts with
importance and validation mode. Reviews score family-invariant and shot-specific results separately.

### Production workload relationship

Phase 3 does not create a second queue model. Each shot version compiles to a Phase 2
`ProductionIntentSnapshot` and `ProductionWorkloadItem`; attempts/derivatives retain exact location
state, visual plan, shot, invariant set, and control manifest IDs/hashes. B-101 placement references
the approved derivative and shot placement intent.

### `SceneSpatialControlProfile`

Persisted in Model Manager: supported control kinds, model artifacts, preprocessors, per-control
weights/start/end values, workflow/node revisions, checkpoint compatibility, enabled status.
Every submitted value is configured; none is inferred from a code default.

## Coordinate Contract

- Right-handed coordinates.
- Centimeters for positions and dimensions.
- Y-up world axis.
- Quaternions for persisted rotations, normalized on write.
- Transforms contain position, rotation, and scale; scale must be positive.
- Cameras store world transform and explicit projection values.

The editor converts to/from Three.js without changing the persisted convention.

## Staleness

| Change | Invalidates |
|---|---|
| New identity pack, actor/object/relationship/landmark/world transform | New visual-plan version; all shots/controls on old plan remain historical but are not current. |
| Camera, crop, or visible-set change | New shot version; only its controls become stale. |
| Compiler or configured control profile change | New manifest required for every affected shot. |
| Location-state version change | New visual-plan version; prior plan/shot/control lineage remains historical. |

Staleness is computed by exact version IDs and `InputHash`, never by timestamps alone.

## State Machines

- Location/visual plan: `Draft -> Approved -> Superseded`.
- Shot: `Draft -> Frozen -> Superseded`.
- Manifest: `Pending -> Compiling -> Complete|Failed|Stale`.
- Scene-controlled render attempt uses the Phase 2 monotonic attempt states.

## Clean-Baseline Strategy

Create Phase 3 tables/indexes for new production sessions only. Do not backfill old images, create
synthetic visual plans, add nullable compatibility provenance, dual-write, or retain prompt-only
runtime modes. Older sessions fail before Phase 3 mutation with create-new-session guidance.
