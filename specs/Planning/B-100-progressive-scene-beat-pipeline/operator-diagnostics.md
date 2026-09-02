# B-100 Diagnostics Operator Guide

## Configuration Source

Catalogue, Beat Production, Moment Discovery, and Moment Enrichment diagnostics use the single
required `FunctionModelDefaults` row for `AppFunction.RolePlaySceneBeatAnalyzer`. In Model Manager,
open **RP Scene Beat Analyzer** and set **Diagnostics Retention Days**. The valid persisted range is
1 through 3650 days.

`SceneBeatAnalyzerResolver.ResolveAsync` validates that row and returns the exact
`FunctionDefaultId` and `DiagnosticsRetentionDays`. Missing or invalid configuration throws a
`ModelResolutionException` with Model Manager guidance. There is no default day count, alternate
profile, optional retention argument, or null-skip path.

## Read Diagnostics

Resolve `ISceneBeatDiagnosticsService` from an application scope:

- `GetMetricsAsync()` returns all four stages in canonical order.
- `GetRecentDiagnosticsAsync(stage, limit)` returns exact owner/attempt/job provenance, status,
  model/provider, finish/validation category, duration, character totals, timestamps, and retained
  flags. It does not return raw response or reasoning text.

Use this service rather than a static UNION query: canonical stage tables are created lazily, and
the repository's exact `sqlite_master` checks are what make an absent stage a valid zero count
without hiding other SQLite failures.

## Prune Expired Raw Diagnostics

The operator/admin host resolves `ISceneBeatDiagnosticsService` in a scope and invokes
`PruneExpiredAsync(actor)` with a nonblank auditable actor. This synchronous command resolves the
analyzer configuration on every run, computes `cutoffUtc = TimeProvider.UtcNow - configured days`,
and performs one transaction across the four canonical attempt tables.

Only `RawModelResponse` and `ReasoningContent` are set to `NULL`, and only for terminal
`Complete`, `Failed`, `Superseded`, or `Cancelled` attempts older than the cutoff. System/user
prompts, finish reason, validation details, duration, input/output counts, owner/job lineage, and all
execution timestamps remain unchanged. Queued/Processing and recent attempts are untouched.

Every invocation, including a zero-row run, appends `SceneBeatDiagnosticsPruneRuns` with the exact
function-default ID, configured days, cutoff, run time, actor, and per-stage counts. Absent canonical
attempt tables count as zero because no attempts exist for that stage; arbitrary SQLite errors are
not swallowed. Legacy `SceneImageBeatAnalyses` is outside this repository and is never pruned.