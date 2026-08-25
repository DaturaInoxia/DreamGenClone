# Phase 3 Data Model - Location and Multi-POV

## Versioned Aggregate Roots

### `LocationVisualProfile`

Fields: `Id`, `LocationKey`, `Name`, `Version`, `Status`, `WidthCm`, `LengthCm`, `HeightCm`,
`CoordinateConvention`, `Description`, `VisualStyleJson`, `LightingIntentJson`, `ExclusionsJson`,
`SupersedesId`, timestamps.

Children:

- `LocationLandmark`: stable `LandmarkKey`, type, dimensions, transform, material/appearance JSON,
  permanence, and evidence.
- `LocationReferenceAsset`: same integrity/provenance contract as Phase 2 references.

### `SceneVisualPlan`

Fields: `Id`, `ScenarioId`, `SessionId`, `InteractionId`, `BeatAnalysisId`, `BeatKey`, `Version`,
`Status`, `LocationProfileId`, `WorldStateJson`, `EvidenceSnapshotJson`, `SupersedesId`, timestamps.

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
`CropIntent`, `VisibleKeysJson`, `OcclusionNotes`, `SupersedesId`, timestamps.

### `SceneControlAsset`

Fields: `Id`, `SceneShotPlanId`, `ControlKind` (`Depth`, `Pose`, `ActorRegion`, `SemanticMask`,
`Preview`), optional `SemanticKey`, file integrity fields, `CompilerName`, `CompilerVersion`,
`InputHash`, timestamps.

### `SceneControlManifest`

Fields: `Id`, `SceneShotPlanId`, `ManifestVersion`, `Status`, `CompilerVersion`, `InputHash`,
`ManifestJson`, `CreatedUtc`. The manifest lists exact control IDs, dimensions, preprocessing,
configured model artifacts/weights, and compatibility data.

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
| Prompt-only text edit | Does not mutate the source visual plan. |

Staleness is computed by exact version IDs and `InputHash`, never by timestamps alone.

## State Machines

- Location/visual plan: `Draft -> Approved -> Superseded`.
- Shot: `Draft -> Frozen -> Superseded`.
- Manifest: `Pending -> Compiling -> Complete|Failed|Stale`.
- Scene-controlled render attempt uses the Phase 2 monotonic attempt states.

## Migration

Add tables and indexes without backfilling plans for existing Phase 1 images. Add nullable provenance
foreign keys to render-attempt data only if needed; existing images stay `PromptOnly`.
