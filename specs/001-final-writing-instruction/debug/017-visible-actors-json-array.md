# Debug 017: Visible Actors Must Be a JSON Array

## Report

Scene Production failed while creating the first durable revision for session
`4f2eec18-b190-4beb-ad35-8d520ae5c800`. The Studio reported `Visible actors must be a JSON array.`
The failure occurred after the Scene Production context ID fix and prevented durable workspace creation.

## Analysis

The controlling validation path is `ProductionMediaCompilerBase.ActorCount` in
`DreamGenClone.Web/Application/RolePlay/ProductionMediaCompilers.cs`. It requires
`ProductionIntentSnapshot.VisibleActorsJson` to parse as a non-empty JSON array.

`SceneImageStudio.razor` populated that field with the complete
`CompiledMediaBrief.SemanticInputSnapshotJson`. The canonical still semantic snapshot is an
object containing `lineage`, `moment`, `frozenState`, `continuity`, `typedReferences`, and
`videoKeyState`; its canonical participant array is nested at `moment.participantSummary`.

The authoritative still brief shape is produced by
`DeterministicMultimodalMediaCompiler.BuildStill`. The production compiler then consumes
`VisibleActorsJson` as a separate array-shaped intent field.

Relevant specification references: `specs/001-final-writing-instruction/spec.md`,
`specs/001-final-writing-instruction/plan.md`, and the durable production compiler contract
implemented by `ProductionMediaCompilerBase.ActorCount`.

## Plan

Extract `moment.participantSummary` from the compiled semantic snapshot in the Scene Production
bootstrap, serialize that array as `VisibleActorsJson`, preserve the full semantic snapshot in
`ContextSnapshotJson`, and fail explicitly if the canonical participant array is missing, malformed,
or empty. Validate with a Web build, focused production tests, and a fresh Studio runtime action.

## Resolution

Updated `SceneImageStudio.razor` to call `ExtractVisibleActorsJson` when constructing the durable
intent. The helper parses the compiled semantic snapshot, requires a non-empty
`moment.participantSummary` JSON array, and returns that array's raw JSON. Invalid JSON and invalid
shape now fail before intent persistence with explicit diagnostics.

## Validated

[x] Web project build succeeded with 0 errors.
[x] Focused production tests passed: 36 passed, 0 failed.
[ ] Fresh browser bootstrap and persisted workload verification pending.
