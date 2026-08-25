# Phase 3 Contracts - Location and Multi-POV

## Repository Contracts

Use aggregate-specific repositories for location profiles and scene plans. Reads needed by job
handlers return complete immutable snapshots by exact ID/version. Approval and supersession are
explicit methods with optimistic version checks.

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
It does not submit an image job.

## Scene-Controlled Render Contract

The request extends the Phase 2 controlled request with exact control manifest and asset streams.
The ComfyUI client receives already resolved model artifacts, weights, start/end percentages, and
preprocessor outputs. It builds the one pinned workflow proven for Phase 3. It never searches for
another ControlNet model or removes a failed control.

## Visual Plan Compiler

An optional configured text model may propose a draft from beat/location/character evidence using
a strict JSON schema. Deterministic validation rejects unknown actor keys, unsupported predicates,
missing evidence, out-of-bounds transforms, and invented identity/location references. The user
must approve a plan before controlled compilation.

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

## Browser Proof Contract

Verify desktop and mobile Studio behavior with screenshots and canvas-pixel checks. The canvas must
be nonblank, controls must not overlap, the active camera framing must match the generated preview,
and reload must preserve transforms within the documented numeric tolerance.
