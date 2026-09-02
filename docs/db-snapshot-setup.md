# Setting up DreamGenClone on another machine (database)

> **Complete step-by-step (clone → DB → build → run → keys → verify): see [`docs/setup-other-machine.md`](setup-other-machine.md).**
> This file covers the database-specific details.

## What you get from git
The repo tracks exactly **one** database file: `DreamGenClone.Web/data/dreamgenclone.snapshot.db`

- It's a **sanitized snapshot** of the working database: all themes, scenarios, characters, tones, styles, profiles — but **no API keys**.
- The live working database (`dreamgenclone.dev.db`) is git-ignored, so it is **not** in the repo. It's created from the snapshot below.

## One-time setup
1. Clone the repo:
   ```powershell
   git clone <your-repo-url>
   cd DreamGenClone
   ```
2. Create your working database from the snapshot:
   ```powershell
   copy DreamGenClone.Web\data\dreamgenclone.snapshot.db DreamGenClone.Web\data\dreamgenclone.dev.db
   ```
   (macOS/Linux: `cp DreamGenClone.Web/data/dreamgenclone.snapshot.db DreamGenClone.Web/data/dreamgenclone.dev.db`)
3. Apply host-local data configuration commands:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\helpers\dbq.ps1 b100-analyzer-configure
   ```
   This idempotently assigns `DeepSeek / deepseek-v4-flash` to `RolePlaySceneBeatAnalyzer` and persists all required analyzer settings. It fails without changing the database if that enabled provider/model pair is unavailable or ambiguous.
4. Start the app:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\helpers\start-webapp-dev-clean.ps1
   ```
   Opens `http://localhost:5177` in Development mode.
5. Re-enter your provider API keys once:
   Web app → **Settings → Providers** (OpenRouter, TogetherAI, DeepSeek, Infermatic, etc.)

## Keeping your working DB safe
- `dreamgenclone.dev.db` is git-ignored. A `git pull` will **never** overwrite it.
- **NEVER run `git clean -fd` or `git clean -fdx`** — this deletes ignored files, including your working database.
- Always start the app from the `DreamGenClone.Web` folder in Development mode (the helper script does this). If you launch the DLL manually, run it from `DreamGenClone.Web` with `ASPNETCORE_ENVIRONMENT=Development`, otherwise the app opens a **different, empty database**.

## Refreshing content from git later
To pull in the latest content after it was updated upstream:
```powershell
copy DreamGenClone.Web\data\dreamgenclone.snapshot.db DreamGenClone.Web\data\dreamgenclone.dev.db
```
This overwrites `dev.db` with the snapshot content. Because the snapshot has no keys, re-enter your API keys afterward.

## Sharing configuration changes
Do not copy `dreamgenclone.dev.db` over the tracked snapshot. The live database contains encrypted provider API keys and session/debug data.

Portable configuration changes must be implemented as reviewed, idempotent named commands in `DreamGenClone.DbQuery`, then run once on each host after its working database is created. The B-100 analyzer uses `b100-analyzer-configure` for this purpose.

There is currently no supported snapshot-refresh command. Treat `dreamgenclone.snapshot.db` as the existing sanitized base until a sanitizer with explicit credential and session-data verification is added.
