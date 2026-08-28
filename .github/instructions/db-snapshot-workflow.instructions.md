---
applyTo: 'DreamGenClone.Web/data/**,artifacts/tmp/dbquery/**,docs/db-snapshot-setup.md,helpers/start-webapp-dev-clean.ps1,helpers/start-webapp.ps1'
description: 'DB snapshot workflow: why the dev DB grows, the two-DB model (live dev.db vs git-tracked snapshot.db), git safety rules, correct app startup, snapshot refresh, and other-machine setup.'
---

# DB Snapshot Workflow — Complete Reference

## Why the dev database grows
- `RolePlayDebugEvents.MetadataJson` stores FULL built LLM prompts (600 KB+ per `PromptBuilt` event). ~138k events ≈ 1.37 GB, never pruned automatically.
- Other session-runtime bloat: `Sessions` (PayloadJson/AdaptiveStateJson), `RolePlaySemanticInteractionAnalysisState` (full prompts), `RolePlayV2EncounterSummaries` (LLM summaries + enrichment prompts).
- The actual content (characters/scenarios/themes/profiles/config) is **< 1 MB** total; story data ~7 MB.

## Two-DB model
| File | Role | Git |
|---|---|---|
| `DreamGenClone.Web/data/dreamgenclone.dev.db` | Live working DB, has encrypted API keys | **IGNORED** — never tracked |
| `DreamGenClone.Web/data/dreamgenclone.snapshot.db` | Sanitized copy, keys blanked | **TRACKED** — the only DB in git |

The committed `snapshot.db` is a separate point-in-time copy, NOT the live DB. `dev.db` and `snapshot.db` are identical except: API keys (blanked in snapshot), and any timestamps/health-check rows written by the app after the snapshot was taken.

## Hard rules
1. **NEVER commit `dreamgenclone.dev.db` or any `.db`/`.bak` file** — only `dreamgenclone.snapshot.db` is allowed in git.
2. **NEVER run `git clean -fd` / `git clean -fdx`** — it deletes ignored files, including the live `dreamgenclone.dev.db`.
3. `git pull` never touches `dev.db` (it is ignored); it only updates `snapshot.db` and the rest of the repo.
4. App DB path is relative to cwd + environment: **Development → `data/dreamgenclone.dev.db`**, **Production → `data/dreamgenclone.db`**. Always start the app from `DreamGenClone.Web` with `ASPNETCORE_ENVIRONMENT=Development` (as `helpers/start-webapp-dev-clean.ps1` does). Starting it from the repo root, or without the env var, opens the WRONG near-empty DB.
5. Never point the app at `snapshot.db` as its working DB; the snapshot is only for cloning/restoring on a fresh machine.

## Refreshing the snapshot (when content changed)
```
python artifacts/tmp/dbquery/create_seed_db.py
```
Copies current `dev.db` → `snapshot.db`, then **automatically drops ALL session-runtime/debug data** (same table list as `prune_sessions.sql`), blanks `ApiKeyEncrypted`, and VACUUMs — so the snapshot stays small no matter how much session data has accumulated in `dev.db`. The working `dev.db` is never modified and keeps its sessions for debugging. Then commit `snapshot.db`.

## Pruning the dev DB (only when explicitly asked)
- Stop the web app first (it locks the DB).
- Prune session tables: `helpers/dbq.ps1 exec artifacts/tmp/dbquery/queries/prune_sessions.sql`, then `VACUUM`.
- Content tables (characters/scenarios/themes/profiles/config/story) are never touched by the prune.

## Other machine setup
See `docs/db-snapshot-setup.md` (ships in the repo). Short version:
1. `git clone <repo>` (gets `snapshot.db` + full content)
2. `copy DreamGenClone.Web\data\dreamgenclone.snapshot.db DreamGenClone.Web\data\dreamgenclone.dev.db`
3. Start via `helpers/start-webapp-dev-clean.ps1`
4. Re-enter provider API keys once (Settings → Providers)

## Tooling
- Use `helpers/dbq.ps1` / `helpers/dbq-session.ps1` for all DB queries against `dreamgenclone.dev.db`. See `dbquery-reference.instructions.md`.
- Helper scripts for size analysis, snapshot creation, and DB diffing live in `artifacts/tmp/dbquery/*.py`.
