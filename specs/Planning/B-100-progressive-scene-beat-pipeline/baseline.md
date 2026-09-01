# B-100 Benchmark Baseline

**Status:** T003 remains open.

A configured-model benchmark was attempted on 2026-08-31. It failed before any model request because the live development database has no `FunctionModelDefaults` row for `RolePlaySceneBeatAnalyzer`. The sanitized failure reports are `artifacts/tmp/b100-corpus/b100-corpus-report-20260831.json` and `artifacts/tmp/b100-corpus/b100-corpus-report-20260831.md`.

No stage was attempted and no latency or validity result was produced. T003 remains open. The missing function configuration must be explicitly saved through Model Manager; copying another function's settings or selecting a model in runner code would violate the no-fallback configuration contract.

## Historical Context

Existing diagnostics summarized in `analysis.md` cover 19 completed historical Beat jobs:

- Average duration: 127.9 seconds.
- Median duration: 139.0 seconds.
- Maximum duration: 244.9 seconds.
- Representative successful completion: 89,468 ms model completion and 89.6 seconds surrounding job duration.
- Representative failed completion: 147,636 ms before failure.

These observations predate the frozen corpus and do not provide per-stage validity, Catalogue, Beat Production, Moment Discovery, or Moment Enrichment percentiles. They must not be presented as the T003 baseline.

## Capture Command

From the repository root:

```powershell
.\helpers\run-b100-corpus-benchmark.ps1 -Iterations 1
```

Bounded diagnostics can use `-Case <case-id>` and `-Stage Catalogue|BeatProduction|MomentDiscovery|MomentEnrichment`. A stage selection executes its required upstream lineage but evaluates only the selected stage gate. Reports include duration, strict validity/failure category, finish reason, lineage, and output character count; they never include prompts, raw model output/reasoning, provider URLs, or credentials.

The runner reads model/provider/function configuration from `DreamGenClone.Web/data/dreamgenclone.dev.db` in SQLite read-only mode. Stage records are written only to fresh temporary SQLite databases and are deleted with their WAL/SHM files unless `-KeepWorkingDb` is supplied. Reports default to `artifacts/tmp/b100-corpus-report-<UTC>.json`.

T003 can close only after a genuine configured-model run produces a retained report. T065, T082, T099, T119, and T172 remain open until that report passes their fixed latency and validity gates.

The non-secret configuration diagnostic is:

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/b100-analyzer-configuration.sql
```

The 2026-08-31 diagnostic returned no rows. It intentionally does not select the encrypted credential. The full wrapper execution exited nonzero with `configuration_resolution_failed`, zero model calls, zero attempted stages, and every real-model gate unmeasured/failed.
