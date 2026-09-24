# 058 — B-127 P0a part 2a: the identity re-key preview (`b127-identity-rekey preview`)

**Status:** Delivered — read-only command in the permanent DbQuery dispatcher; run against the dev DB, output
captured; app and test suite untouched
**Date:** 2026-09-22
**Items:** B-127 P0a part 2 (first half; the applied re-key + the code switch are the second half)

## Report

The applied re-key **renames** the identity key columns (`CharacterProfileId` → `CharacterTemplateId`). Running that
before the code switch would leave an app that cannot read its own stores, and flipping resolution before the rows
are re-keyed would hide a character's existing packs (Dean's 8 are keyed by the instance id). So the destructive half
ships as one step with the code switch, and this deliverable is the half that is safe to run now: the **preview**,
which answers the question the operator has to answer first.

`dotnet run --project DreamGenClone.DbQuery -- b127-identity-rekey preview`

Read-only (`SqliteConnection` is opened `Mode=ReadOnly` by the dispatcher for this command — it is not in the
ReadWrite allowlist). Resolution follows the app's one rule set: a character template id, else a scenario
character's `TemplateId`, else an explicit link row, else **unresolved** — never a name. Exit code **3** means "the
apply would refuse"; 0 means the plan is clean.

## What it found in the dev DB (2026-09-22)

| Owner | Is | → template | Packs | Builds |
|---|---|---|---|---|
| `f58f959a…` | scenario character 'Becky' in Campground Intimacy | `de351eb3…` Becky | 5 (v5 approved) | 1 |
| `faee1ec0…` | scenario character 'Dean' in Campground Intimacy | `a4894571…` Dean | 8 (v8 approved) | 1 |
| `a9e0782a…` | scenario character 'Dean' in The Party | `a4894571…` Dean | 1 (v1 draft) | 0 |
| `a9137ebf…` | **character asset 'Sam', no link row** | **UNRESOLVED** | 0 | 2 |

Merge plan: **Dean 9 packs → v1…v9** by `CreatedUtc` (the Campground chain shifts +1, The Party's draft lands at
**v2**), `SupersedesId` links all stay inside the template so none is cleared, and exactly **one** approved pack
remains. **Becky 5 packs → unchanged numbering.** Column rename: the four identity tables; `SceneAssets.CharacterProfileId`
is explicitly left alone (it records the origin instance, not ownership).

## The hazard the preview caught (this is why it exists)

After merging, Dean holds an **approved v9 with a draft at v2 below it**. `GetLatestApprovedPackAsync` returns the
highest-versioned approved pack, so approving that draft later would *not* become the character's active pack — a
silent trap created by the merge and visible nowhere else. The preview reports it and names the explicit resolution
(`--demote-shadowed-drafts`, i.e. mark such drafts `Superseded`); the apply will **refuse** the shape without that
flag. Nothing about it is chosen automatically.

## Evidence

- Preview run against `data/dreamgenclone.dev.db` (exit code 3, blocked by Sam), full output captured at
  `artifacts/tmp/b127-rekey-preview.txt` (git-ignored).
- Nothing was written: the command is read-only, and the dev DB's identity tables are unchanged (verified by ids,
  versions and statuses matching the earlier audits).
- The app was not restarted, no test file changed; the suite result from `debug/057` still stands.

## Two decisions the operator has to make before the apply

1. **Sam** (`a9137ebf…`, 2 builds): which character template owns it? It is an asset-owned character with no link
   row, so the apply refuses until it is linked explicitly — either through the app's *Link character identity*
   action (P0b) or by passing `--link a9137ebf…=<template id>` to the apply.
2. **Dean's shadowed draft** (`c41ae322…`, from The Party, landing at v2): demote it to `Superseded`
   (`--demote-shadowed-drafts`), or leave the merge blocked until it is dealt with another way (for example
   promoting the Campground chain again afterwards).

## Next (second half of P0a part 2, one step)

The apply's writes (rename + re-key + renumber + the explicit `--link` / `--demote-shadowed-drafts` handling) land
**together with** the code switch: rename `CharacterProfileId` → `CharacterTemplateId` across packs/builds/body
cards/LoRA datasets (domain, repositories, `ALTER TABLE … RENAME COLUMN` guards) and route the service resolution
sites through `ICharacterIdentityOwnerResolver`. Then P0b (surfaces), P2 (consumers), P3 (close-out).
