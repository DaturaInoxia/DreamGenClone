# Phase 3 Contracts - Location and Multi-POV

## Repository Contracts

Use aggregate-specific repositories for location profiles and scene plans. Reads needed by job
handlers return complete immutable snapshots by exact ID/version. Approval and supersession are
explicit methods with optimistic version checks.

Location reads return base profile and state versions separately. Shot-family reads return one
approved visual-plan snapshot, its typed invariant set, and exact shot versions; no compiler or
dispatcher resolves `latest` mutable data.

## Three.js Interop Contract

Blazor sends a `BlockingSceneDto` containing primitives/assets, semantic keys, transforms, joints,
relationships, and cameras. JavaScript returns only validated domain DTOs and editor commands.

Required interop operations:

- initialize/dispose editor;
- load blocking scene;
- select semantic key;
- apply transform/joint/camera command;
- undo/redo;
- export current blocking DTO;
- render preview/depth/pose/semantic/actor-region buffers.

No Three.js UUID or serialized object graph is a database identity. Event subscriptions, renderer,
controls, textures, and object URLs must be disposed when the component is removed.

## Control Compiler Contract

```csharp
public interface ISceneControlCompiler
{
    Task<SceneControlCompilationResult> CompileAsync(
        SceneVisualPlanSnapshot visualPlan,
        SceneShotPlanSnapshot shot,
        ResolvedSpatialControlProfile profile,
        CancellationToken cancellationToken);
}
```

The result includes files/streams and a canonical manifest. Compilation validates plan/shot
version compatibility, dimensions, visible actor regions, artifact configuration, and input hash.
It does not submit an image job. Every emitted control kind, renderer/preprocessor, and output
convention must be accepted by the exact qualified capability cell used by the workload item.

## Scene-Controlled Render Contract

The Phase 3 compiler produces a Phase 2 `CompiledMediaRequest` from exact visual-plan, shot,
invariant, character/location asset, and control-manifest snapshots. Provider adapters receive an
already validated request. They never reinterpret the shot, search for another control/model, or
remove a failed control.

## Visual Plan Compiler

An optional configured text model may propose a draft from beat/location/character evidence using
a strict JSON schema. Deterministic validation rejects unknown actor keys, unsupported predicates,
missing evidence, out-of-bounds transforms, and invented identity/location references. The user
must approve a plan before controlled compilation. Model output is typed draft data, never a
model-native image/edit prompt.

## Manifest Schema

The canonical manifest includes:

- schema/compiler version and input hash;
- visual-plan and shot IDs/versions;
- output dimensions;
- each control's kind, semantic key, path, checksum, preprocessor, artifact, weight, start/end;
- actor-region to identity-assignment mapping;
- checkpoint/workflow/node revisions;
- creation timestamp.

Sort controls by kind then semantic key before hashing/serialization.

## Shot Family And Presentation Contract

`IShotFamilyReadinessService` evaluates shared invariants, exact shot/control versions, qualified
capability cells, grouping, policy, endpoint readiness, output counts, and cost. It creates normal
Phase 2 workload items; it does not own a second dispatcher.

`IApprovedMediaPlacementSource` exposes approved derivative ID, Moment/version, shot/family key,
placement intent, duration hint where applicable, aspect/crop, and lineage hash to B-101. B-101 may
place and publish that derivative but cannot mutate its plan, attempt, review, or approval.

## Browser Proof Contract

Verify desktop and mobile Studio behavior with screenshots and canvas-pixel checks. The canvas must
be nonblank, controls must not overlap, the active camera framing must match the generated preview,
and reload must preserve transforms within the documented numeric tolerance.
