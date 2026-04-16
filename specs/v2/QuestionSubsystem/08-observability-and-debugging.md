# Observability and Debugging

## Current Limitation

Diagnostics snapshots provide aggregate counts, but skip causality is not fully exposed.

## Required Events

E1. DecisionAttemptStarted

- session id
- correlation id
- interaction index
- actor context summary

E2. DecisionAttemptEvaluated

- cadence gate result
- cooldown gate result
- phase gate result
- candidate count
- rejected candidate reasons

E3. DecisionCreated

- decision id
- owner actor id/name
- target actor id/name
- transparency mode

E4. DecisionSkipped

- skip reason codes
- dominant reason

E5. DecisionApplied

- selected option id
- before/after stat snapshots per affected actor

E6. DecisionPersistenceFailure

- exception type
- schema context

## Skip Reason Codes

- `CadenceCooldownActive`
- `CadenceWindowNotReached`
- `NoEligibleTemplates`
- `AllOptionsFailedPrerequisites`
- `ActorResolutionFailed`
- `PersistenceBlocked`

## Logging Principles

- One attempt summary per continuation cycle.
- Structured logs only for machine analysis.
- Include stable codes, not only free-text messages.
