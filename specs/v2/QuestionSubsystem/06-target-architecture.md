# Target Architecture

## Overview

The target architecture keeps existing service boundaries but formalizes a decision-attempt pipeline with explicit gate outcomes.

## Pipeline Stages

1. Context Assembly
- Build actor, scenario, phase, and stat context.

2. Eligibility Evaluation
- Evaluate cadence, cooldown, and phase constraints.
- Emit per-gate pass/fail reasons.

3. Candidate Generation
- Score templates/options against context.
- Track rejected candidates with reasons.

4. Decision Construction
- Resolve asking actor and target actor.
- Materialize transparency-adjusted stat deltas.

5. Persistence
- Persist decision with full metadata fields.
- Validate write and idempotency behavior.

6. Presentation
- Surface decision in UI with owner/target labels.

7. Application
- Apply selected option deltas to intended actors only.
- Emit before/after stat snapshots for diagnostics.

## Contracts to Preserve

- Existing `DecisionPointService` responsibilities remain intact.
- Existing persistence repository APIs remain additive-compatible.
- Existing UI component remains the consumer of finalized decision DTOs.

## New Contract Additions

- DecisionAttemptDiagnostics payload per cycle.
- Structured SkipReason enum set with machine-readable codes.
