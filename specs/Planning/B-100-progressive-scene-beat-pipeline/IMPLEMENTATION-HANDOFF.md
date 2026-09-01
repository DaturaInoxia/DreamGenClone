# B-100 Implementation Handoff

## Current state

B-100 is implemented through the deterministic pipeline, durable jobs, strict stage contracts, diagnostics, production integration, and the standalone real-model corpus runner. Direct DeepSeek Flash is selected through the canonical `RolePlaySceneBeatAnalyzer` function configuration. No OpenRouter or runtime fallback path is used.

Local verification on 2026-08-31:

- `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore` passed: 1,633 succeeded, 0 failed, 0 skipped.
- Focused structured-output, resolver, persistence, and corpus-runner tests pass.
- Web, DbQuery, and CorpusRunner builds pass.
- The live database resolves the enabled direct `DeepSeek / deepseek-v4-flash` model in `JsonObject` mode with function-level `MaxTokens=4000`.

The remaining acceptance work is the real-model corpus run on a machine/network that can reach DeepSeek.

## Why the real-model gate is still open

This machine cannot complete TLS negotiation with DeepSeek. Both `api.deepseek.com` and `www.deepseek.com` fail before an HTTP response with Windows Schannel `SEC_E_ILLEGAL_MESSAGE`; an unrelated HTTPS control request returns HTTP 200. The B-100 runner therefore records `structured_text_transport_failure` for Catalogue and correctly leaves downstream stages unattempted.

This is a host/network connectivity blocker, not evidence of an invalid API key, model identifier, JSON Object request, or model output. Do not switch providers or add a fallback to work around it.

## Other-machine database setup

There are two database roles:

- `DreamGenClone.Web/data/dreamgenclone.snapshot.db` is the sanitized, tracked base. It contains no API keys.
- `DreamGenClone.Web/data/dreamgenclone.dev.db` is the ignored live database. It contains host-local encrypted credentials and must never be committed.

From the repository root on the other machine:

```powershell
# Only when dev.db does not already exist or may be intentionally replaced.
Copy-Item .\DreamGenClone.Web\data\dreamgenclone.snapshot.db `
  .\DreamGenClone.Web\data\dreamgenclone.dev.db

# Apply the portable, idempotent B-100 analyzer configuration.
powershell -ExecutionPolicy Bypass -File .\helpers\dbq.ps1 b100-analyzer-configure
```

The named command requires exactly one enabled direct `DeepSeek / deepseek-v4-flash` provider/model pair. It sets `StructuredOutputMode=JsonObject` and upserts the complete `RolePlaySceneBeatAnalyzer` function settings. It is transactional and fails without modifying the database if the required pair is missing or ambiguous.

After copying the sanitized snapshot, start the app with `helpers/start-webapp-dev-clean.ps1`, open Settings -> Providers, and enter the DeepSeek API key on that machine. Never put the key in a command, handoff document, snapshot, report, or git-tracked file.

Do not overwrite an existing `dev.db` casually: doing so removes its local credentials and working data. Never run `git clean -fd` or `git clean -fdx`; either command deletes the ignored live database.

## Real-model verification

First verify that the other machine can negotiate TLS with DeepSeek without sending credentials:

```powershell
curl.exe -sS -o NUL -w "HTTP %{http_code}`n" https://api.deepseek.com/v1/models
```

Any HTTP status, including 401, proves DNS/TLS reachability. `HTTP 000` or a TLS error means the machine/network is still blocked.

Run one cheap Catalogue request first:

```powershell
.\helpers\run-b100-corpus-benchmark.ps1 `
  -Iterations 1 `
  -Case solo-workshop `
  -Stage Catalogue
```

If that produces a valid Catalogue result, run the complete frozen corpus:

```powershell
.\helpers\run-b100-corpus-benchmark.ps1 -Iterations 1
```

Reports are written to `artifacts/tmp/b100-corpus-report-<timestamp>.json` with a Markdown companion. Reports contain sanitized configuration identity and stage evidence, not provider credentials or raw secrets.

The runner intentionally exits nonzero when a configured gate fails. Inspect the latest report rather than treating every exit code 1 as a runner defect.

## Invariants for follow-up fixes

- Keep direct DeepSeek Flash; do not route B-100 through OpenRouter.
- Resolve analyzer behavior only from the UI-backed `RolePlaySceneBeatAnalyzer` function default and selected model/provider capability data.
- Use the configured function-level `MaxTokens=4000`; optional model token limits are constraints only when populated.
- Keep exactly one structured-output mode per model. Direct DeepSeek uses `JsonObject`; strict-schema-capable providers may use `StrictJsonSchema`.
- Do not retry with another protocol, provider, model, guessed token limit, or hidden default.
- Preserve strict parsing and semantic validation. Do not repair malformed model output silently.
- Missing or invalid configuration must fail explicitly.

## Key entry points

- Analyzer resolution: `DreamGenClone.Web/Application/RolePlay/SceneBeatAnalyzerResolver.cs`
- Structured transport: `DreamGenClone.Infrastructure/Models/OpenAiStructuredTextCompletionClient.cs`
- Durable analyzer snapshot: `DreamGenClone.Application/RolePlay/SceneBeatPipelineContracts.cs`
- Portable DB command: `DreamGenClone.DbQuery/Program.cs` (`b100-analyzer-configure`)
- Corpus runner: `DreamGenClone.CorpusRunner/`
- PowerShell wrapper: `helpers/run-b100-corpus-benchmark.ps1`
- Acceptance evidence: `phase-13-validation.md`
- Full database setup: `docs/setup-other-machine.md` and `docs/db-snapshot-setup.md`

## Completion condition

Run the full corpus against direct DeepSeek on the reachable machine, review stage validity and latency gates in both generated reports, and update `phase-13-validation.md`, `baseline.md`, and `tasks.md` with the actual evidence. Do not mark the real-model acceptance tasks complete unless the reports prove them.