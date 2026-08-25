# Setup DreamGenClone on a New Machine — Complete Reference

**Purpose**: Exact steps to recreate this project on another machine (fresh OS, no prior setup).
**Source of truth**: This file + `docs/db-snapshot-setup.md` (DB-specific) + `specs/Planning/B-032-scene-image-generator/RESUME-HANDOFF.md` (if resuming the scene-image work).
**Last verified**: 2026-08-20 — snapshot `dreamgenclone.snapshot.db` is refreshed and pushed (commit `d41b2fd`).

---

## 0. Prerequisites on the new machine

- [ ] Git
- [ ] .NET SDK **9.0** (`dotnet --version` → `9.x`)
- [ ] Python 3 (only needed for the DB snapshot/prune helper scripts)
- [ ] (Optional, for the browser auto-open helper) nothing extra — the helper uses the default browser

Check:
```powershell
dotnet --version   # expect 9.x
git --version
python --version   # optional
```

---

## 1. Clone the repo

```powershell
git clone <your-repo-url>
cd DreamGenClone
git checkout 001-scene-image-generator    # if that branch has the latest work
git pull
```

> The repo tracks exactly **one** DB file: `DreamGenClone.Web/data/dreamgenclone.snapshot.db` (sanitized, key-free). The live `dreamgenclone.dev.db` is git-ignored and is created in step 3.

---

## 2. (Optional) Python environment for helper scripts

The DB helper scripts (`artifacts/tmp/dbquery/*.py`) run on plain `python` — no packages needed. If you want a venv for consistency:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
```

(You do **not** need the venv to run the app — it's only for the DB tooling.)

---

## 3. Create your working database from the snapshot

```powershell
copy DreamGenClone.Web\data\dreamgenclone.snapshot.db DreamGenClone.Web\data\dreamgenclone.dev.db
```

- This gives you: all themes (17), scenarios (3), providers (6), character profiles, story data, image-model config (Seedream-4.0 as `ModelKind=Image`, TogetherAI as image-capable), and the full scene-image schema.
- **Sessions are NOT included** (sanitized by design). If you need your RP sessions, copy the `dreamgenclone.dev.db` file from the original machine manually (it holds encrypted API keys, so it can't go through git).

---

## 4. Build (must be 0 errors)

```powershell
dotnet build DreamGenClone.sln
```

---

## 5. Start the app

**Always start from the `DreamGenClone.Web` folder with `ASPNETCORE_ENVIRONMENT=Development`** — otherwise the app opens a different, empty database.

Easiest — use the helper (Development, port 5177, opens browser):

```powershell
powershell -ExecutionPolicy Bypass -File .\helpers\start-webapp-dev-clean.ps1
```

Manual equivalent:

```powershell
cd DreamGenClone.Web
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run
```

App: `http://localhost:5177`

---

## 6. Re-enter provider API keys (one-time)

Web app → **Settings → Providers** → edit each provider (OpenRouter, TogetherAI, DeepSeek, Infermatic, LM Studio, Local) and re-enter the API key.

The snapshot blanks `ApiKeyEncrypted`, so keys are **not** in the repo — you must enter them once on this machine.

---

## 7. Verify the app is healthy

- [ ] `http://localhost:5177` loads
- [ ] **Model Manager** (`/model-manager`) shows the image-capable TogetherAI provider and Seedream-4.0 (ModelKind = Image)
- [ ] **Scene Image Studio** route renders: `/roleplay/studio/<sessionId>/<interactionId>`
- [ ] **Gallery** route renders: `/roleplay/gallery/<sessionId>`
- [ ] Run a quick Test Connection on a provider to confirm keys work

---

## 8. Run the test suite (optional but recommended)

```powershell
dotnet test DreamGenClone.Tests\DreamGenClone.Tests.csproj
```

Expected: **all pass** (1157+ tests on the feature branch).

---

## Safety rules (from `docs/db-snapshot-setup.md`)

- **NEVER** `git clean -fd` / `git clean -fdx` — deletes the live `dev.db`.
- `git pull` never touches `dev.db` (ignored); it only updates `snapshot.db`.
- **NEVER** commit `dev.db` or any `.db`/`.bak` — only `dreamgenclone.snapshot.db` is tracked.
- If you need to re-pull content later:
  ```powershell
  copy DreamGenClone.Web\data\dreamgenclone.snapshot.db DreamGenClone.Web\data\dreamgenclone.dev.db
  ```
  then re-enter API keys again.

---

## If you are resuming the Scene Image Generator work

See `specs/Planning/B-032-scene-image-generator/RESUME-HANDOFF.md` — it has the full plan (CR-006 P1→P6), current state, file map, DI registrations, and the domain facts needed to continue.

---

## Quick reference (one block to copy)

```powershell
# 1. clone
git clone <your-repo-url>
cd DreamGenClone
git checkout 001-scene-image-generator

# 2. working DB from snapshot
copy DreamGenClone.Web\data\dreamgenclone.snapshot.db DreamGenClone.Web\data\dreamgenclone.dev.db

# 3. build
dotnet build DreamGenClone.sln

# 4. run (Development, port 5177)
powershell -ExecutionPolicy Bypass -File .\helpers\start-webapp-dev-clean.ps1

# 5. then in the app: Settings -> Providers -> re-enter API keys
```
