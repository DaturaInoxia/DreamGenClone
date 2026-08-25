# Phase 2 Contracts - Character Identity

## Application Interfaces

```csharp
public interface ICharacterImageIdentityRepository
{
    Task<CharacterImageIdentityPack?> GetPackAsync(string packId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CharacterImageIdentityPack>> ListPacksAsync(
        string characterProfileId, CancellationToken cancellationToken);
    Task UpsertDraftAsync(CharacterImageIdentityPack pack, CancellationToken cancellationToken);
    Task ApproveAsync(string packId, CancellationToken cancellationToken);
}

public interface IIdentityConditionedImageClient
{
    Task<ImageGenerationResult> GenerateAsync(
        ResolvedIdentityImageModel model,
        IdentityControlledImageRequest request,
        CancellationToken cancellationToken);
}
```

Names may be adjusted to existing namespace conventions, but generation and source-image editing
remain separate interfaces.

## Controlled Request

`IdentityControlledImageRequest` contains only resolved immutable values:

- positive/negative prompt and image dimensions;
- seed;
- exact checkpoint and workflow revision;
- one or more assignments with actor key, reference stream/path, region stream/path, adapter
  artifacts, and strengths;
- content policy;
- correlation/render-attempt ID.

The client does not query repositories or choose a pack. The job handler compiles the request.

## Resolver Contract

`IIdentityImageModelResolver.ResolveAsync` validates:

- enabled `RolePlaySceneImage` image model using ComfyUI;
- selected mechanism is known and approved by the proof report;
- checkpoint family matches the adapter;
- every artifact name and numeric setting is present;
- regional masks are supported for multi-actor requests;
- content policy is known.

It returns `ResolvedIdentityImageModel`. Validation errors name the missing field and configured
model; no default artifacts, strengths, endpoints, or mechanisms are permitted.

## Storage Contract

Reference and region files use `ISceneImageStorageService` or a narrowly extended asset storage
abstraction with the same path-safety behavior. File ingest computes metadata before DB approval.
Deleting a DB row never deletes a file still referenced by an approved pack or render attempt.

## Job Contract

Add `SceneImageIdentityRendering` and a payload containing `RenderAttemptId`. The handler:

1. exits when already complete;
2. loads and validates exact pack/profile/region versions;
3. marks generating;
4. compiles the request;
5. calls `IIdentityConditionedImageClient`;
6. saves bytes and checksum;
7. stores assignments and provenance;
8. marks complete, or failed with an explicit diagnostic and debug event.

## Host Proof Contract

Before client integration, preserve under `phase-2-character-identity/proofs/identity-conditioning/`:

- host inventory without secrets;
- candidate node/model revisions, licenses, sizes, and hashes;
- API workflow JSON per candidate;
- fixed base prompt, references, masks, poses/views, seeds, and matrix manifest;
- all results and scorecard;
- selected mechanism and rejected alternatives.

No host installation occurs without explicit approval of the dependency and storage plan.
