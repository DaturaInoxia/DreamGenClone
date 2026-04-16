# Requirements and Acceptance

## Functional Requirements

R1. Engine must evaluate question generation on each eligible continuation cycle.

R2. Engine must emit explicit skip reasons when a question is not generated.

R3. Cadence must honor cooldown and threshold rules deterministically.

R4. Actor resolution must support persona and non-persona actors from context.

R5. Decision metadata must persist context summary, asking actor, and target actor.

R6. UI must display question owner and target consistently.

R7. Transparency mode must be configurable and stable per session.

R8. Applying a decision must mutate only intended actors and stats.

R9. Migration path must guarantee required columns before reads/writes.

R10. Diagnostics must be queryable by session id and correlation id.

## Non-Functional Requirements

N1. Decision evaluation path must not materially increase continuation latency.

N2. Diagnostic logging must be bounded and avoid noisy duplication.

N3. Changes must preserve backward compatibility for existing persisted sessions.

## Acceptance Criteria

A1. For seeded replay test sessions, each continuation step reports one of: created, skipped-with-reasons, or errored-with-details.

A2. No occurrences of missing decision metadata columns in integration startup logs.

A3. Multi-actor test fixtures demonstrate at least one non-persona-owned generated decision.

A4. UI snapshot checks confirm ownership/target labels render for all decision cards.

A5. Existing decision mutation tests pass unchanged or with explicitly justified updates.
