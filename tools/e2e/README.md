# DreamGenClone E2E (Playwright)

Playwright tests that drive the **live Blazor Server UI** (`http://localhost:5177`) to validate
the Continuation Settings popup's **Tempo + Span** controls (B-089/B-090).

## Why this exists / what it covers

The xUnit suite (`DreamGenClone.Tests/RolePlay/Prompts/TempoSpanUiFlowTests.cs` and
`TempoSpanDirectiveTests.cs`) deterministically validates the **prompt output** chain
(popup working-copy → override → resolver → `FinalInstructionSlot`). This Playwright project
covers the **real UI layer** those unit tests cannot: clicking the actual buttons, the live
`current:` labels/descriptions, saving through `ReloadAndSaveSessionAsync`, and the sticky
override surviving a popup reopen.

It is **LLM-free by design** — no model turn is ever triggered; the tests only open the popup,
select Tempo/Span, save, and verify persistence. The suite is **non-destructive**: it snapshots
the session's Tempo/Span override once in `beforeAll` and restores it exactly once in `afterAll`,
so the session is left exactly as found (Word Count / Climax Mode / Aftermath / Advanced raw
fields are never touched).

## Prerequisites

1. The webapp must be running on `http://localhost:5177` (Development). The user starts it
   themselves, e.g. `helpers/start-webapp-dev-clean.ps1`.
2. Node.js ≥ 18 (this repo's tooling runs on Windows PowerShell; Node is only needed here).
3. A roleplay session that opens cleanly in the workspace (defaults to the dev session
   `f1d424cc-eb01-47ca-8176-5c280b6fb696`).

## Setup

```powershell
cd tools/e2e
npm install
npx playwright install chromium     # first time only (~downloads the browser)
```

## Run

```powershell
cd tools/e2e
npm test                             # headless
npm run test:headed                  # watch it in a real browser
```

### Configuration (env vars)

| Env var          | Default                                        | Purpose |
|------------------|------------------------------------------------|---------|
| `E2E_BASE_URL`   | `http://localhost:5177`                        | Webapp root |
| `E2E_SESSION_ID` | `f1d424cc-eb01-47ca-8176-5c280b6fb696`         | Workspace session the popup is tested on |

Example against a different session:

```powershell
$env:E2E_SESSION_ID = "aa89a8c2-a743-4bdb-ab51-5d7f654250ee"; npm test
```

## Files

- `playwright.config.ts` — Chromium project, 60 s test timeout, 1 retry, trace on retry.
- `tests/tempo-span-popup.spec.ts` — the Tempo + Span popup flow (select → save → persist → restore).
- `package.json` — `@playwright/test` only; no framework code.

## Notes / limitations

- **Blazor Server circuit attach**: on first navigation the page is prerendered and the
  interactive SignalR circuit isn't attached yet — a click before then is a silent no-op.
  `openPopup` handles this with a probe-retry until the popup actually opens.
- These tests target a **live, stateful Blazor Server app with a real dev DB**. They are a
  UI smoke layer on top of the deterministic xUnit prompt-output suite — not a replacement
  for it, and not a CI-grade hermetic suite (no mock LLM / seed DB yet).
- They are scoped to the popup's Tempo/Span rows; they deliberately never mutate Word Count,
  Climax Mode, Aftermath, or Advanced raw fields, and the suite restores Tempo/Span afterward.
- The "Extended Arc" button label renders as `ExtendedArc` (enum name) in the `current:` label
  and prompt — a known cosmetic inconsistency between the button text and the enum string.
- The verification SQL query `DreamGenClone.DbQuery/queries/e2e-override-restore.sql`
  dumps a session's stored override fields (run via `helpers/dbq.ps1 sql <file> <full-guid>`).
