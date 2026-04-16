# Options and Feasibility

## Option A: Minimal Patching

Description:

- Keep current architecture.
- Add small cadence fixes and sparse logging.

Pros:

- Lowest immediate effort.
- Minimal merge risk.

Cons:

- Does not resolve observability confidence gap.
- High chance of repeated regressions and unclear runtime behavior.

Feasibility: High

Recommendation: Reject as final approach.

## Option B: Instrumentation-First Stabilization

Description:

- Add structured skip-reason telemetry and deterministic cadence assertions first.
- Apply only targeted behavior adjustments after evidence confirms mismatch.

Pros:

- Converts runtime uncertainty into measurable behavior.
- Low-to-medium implementation risk.
- Preserves existing working functionality while enabling safe iteration.

Cons:

- Requires disciplined phased rollout.
- Initial visible feature change is mostly internal diagnostics.

Feasibility: Very High

Recommendation: Adopt as Phase 1-2 foundation.

## Option C: Full Question Engine Rewrite

Description:

- Replace current generation and cadence orchestration with a new subsystem.

Pros:

- Clean-slate architecture potential.

Cons:

- Highest risk and schedule uncertainty.
- Requires full parity revalidation for existing behavior.

Feasibility: Medium-Low

Recommendation: Do not pursue now.

## Selected Path

Choose Option B, then reassess after runtime metrics confirm stable cadence and targeting.
