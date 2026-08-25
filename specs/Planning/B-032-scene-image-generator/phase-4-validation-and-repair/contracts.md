# Phase 4 Contracts - Validation, Repair, and Anchors

## Validator Contracts

```csharp
public interface ISceneDeterministicValidator
{
    Task<IReadOnlyList<SceneValidationFinding>> ValidateAsync(
        SceneValidationContext context,
        CancellationToken cancellationToken);
}

public interface ISceneVisionValidator
{
    Task<SceneVisionValidationResult> ValidateAsync(
        ResolvedVisionValidationModel model,
        SceneVisionValidationRequest request,
        CancellationToken cancellationToken);
}
```

The orchestrating job always invokes deterministic validation first. It skips the vision call only
when deterministic findings make the inputs invalid or the policy explicitly does not require
vision validation.

## Vision Schema

The response root contains `schemaVersion`, `summary`, and `findings`. Every finding contains a
known code, constraint key, subject/object keys where relevant, `pass|fail|unknown`, confidence from
0 to 1, normalized evidence regions, concise rationale, and suggested action. Unknown codes or
keys fail parsing; they are not ignored.

The request includes the rendered image, optional approved anchor crops, a compact expected-
constraint snapshot, and shot visibility. It does not ask the model to reconstruct story facts.

## Policy Resolver

One resolver loads an approved validation-policy version and exactly one configured model for the
requested validator function. It validates all thresholds, repair bounds, action/model mapping,
content-policy compatibility, and provider credentials. Missing configuration fails explicitly.

## Repair Planner

`ISceneRepairPlanner.PlanAsync` accepts an effective finding and exact policy version. It returns
one of:

- `NotEligible` with reason;
- `ReviewRequired` with proposed action;
- `Ready` with exact action, model, instruction, mask, and bound metadata.

It cannot change models, lower a threshold, remove controls, or select prompt-only behavior.

## Attempt Reservation

The repository operation `TryReserveRepairAttemptAsync` is transactional. Inputs include source
attempt, finding, policy, and configured maximum. It returns the reserved record or an exhausted
result. Enqueue happens only after reservation. Failed jobs consume the reserved ordinal because
they are auditable attempts.

## Approval Contract

Approval requires a completed validation run, no unresolved required fail/unknown/conflict after
overrides, and checksum equality with the image currently on disk. Approval is idempotent for the
same image/run/reviewer decision. It never changes validation findings.

## Anchor Contract

Anchor creation accepts an approved frame, explicit scope/entity keys/crops, and usage notes.
Consumers request exact anchor versions. The resolver validates scope compatibility and returns an
immutable snapshot plus asset streams. It never selects “latest” at execution time after a job has
been enqueued.

## Job Types

- `SceneImageValidation` payload: `ValidationRunId`.
- `SceneImageRepair` payload: `RepairAttemptId`.

Both use stable IDs, dedupe keys, scoped dependency resolution, monotonic states, explicit failure
diagnostics, and structured debug events without binary content.
