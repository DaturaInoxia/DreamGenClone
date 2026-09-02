# Phase 4 Contracts - Validation, Repair, and Anchors

## Candidate and Eligibility Contract

A candidate request resolves one approved persisted policy containing candidate count, seed
allocation, and bounds. It creates exactly that many independent render attempts in one candidate
set. Each attempt records its seed and full render provenance; no failed candidate is silently
replaced beyond the configured policy.

Accept/reject is an immutable reviewer decision. Acceptance is one transaction that verifies the
review/validation run and disk checksum, records the candidate decision, and creates its
`ApprovedSceneFrame`. A single eligibility service is the authority for downstream image use and
requires that approved frame. It never treats render `Complete`, validation `Passed`, checksum
success alone, or parent approval as substitutes. Repair children begin ineligible and return to
review.

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

When no proven vision validator is configured for the POC policy, the validation run reports
automation as unavailable and the image proceeds to manual review. The service must not fabricate
a pass or resolve a different provider/model. Pose, person/hand detection, Detailer, SAM, and LaMa
outputs may be attached as advisory evidence only unless their exact decision role has passed the
labeled corpus.

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

A user-directed Qwen repair may be proposed for a localized defect without asserting that Qwen
validated the defect. It still reserves a configured attempt, creates a derived render, preserves
the source, and requires a new review/validation decision.

## Attempt Reservation

The repository operation `TryReserveRepairAttemptAsync` is transactional. Inputs include source
attempt, finding, policy, and configured maximum. It returns the reserved record or an exhausted
result. Enqueue happens only after reservation. Failed jobs consume the reserved ordinal because
they are auditable attempts.

## Approval Contract

Approval requires a completed validation run, no unresolved required fail/unknown/conflict after
overrides, and checksum equality with the image currently on disk. Approval is idempotent for the
same image/run/reviewer decision. It never changes validation findings.

For the manual-gate POC policy, explicit candidate acceptance supplies the human quality decision
and atomically creates the `ApprovedSceneFrame` after the same checksum and provenance guards.
There is no later competing approval path. Descendants and siblings are never accepted implicitly.

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
