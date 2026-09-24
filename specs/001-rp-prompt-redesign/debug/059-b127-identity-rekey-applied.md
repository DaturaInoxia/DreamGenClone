# 059 — B-127 P0a part 2: identity re-keyed onto the character template (applied)

**Status:** Applied to the dev DB (backed up first); solution builds; 263 identity/reference tests green; app
restarted and shown resolving an instance id to the template's identity
**Date:** 2026-09-22
**Items:** B-127 P0a part 2, following `debug/057` (resolver + link store) and `debug/058` (preview)

## Report

The preview (`debug/058`) showed exactly what the re-key would do. This step did it, and it also landed the code
side: the identity key's **meaning** changed from "the scenario instance the studio was opened with" to "the
character template that owns the character".

## What was applied (data — dev DB, backup at `artifacts/tmp/db-backups/dreamgenclone.dev.db.b127-prerekey`)

```
Applied: 16 row(s) re-keyed, 28 version set(s), 1 shadowed draft(s) demoted.
```

| Character | Before | After |
|---|---|---|
| Dean `a4894571…` | 8 packs under `faee1ec0…` (v8 approved) + 1 draft under `a9e0782a…` | **9 packs, v1…v9, exactly one approved (v9)**; the party draft demoted to `Superseded` |
| Becky `de351eb3…` | 5 packs under `f58f959a…` | **5 packs, v1…v5, one approved** — numbering unchanged |
| Sam `a9137ebf…` | 2 builds, 1 prompt override, 1 settings row, no template link | **left exactly as they are** (see below) |

Also re-keyed: the character-scoped rows that are not identity (prompt overrides, per-character settings) — Sam's
two are untouched because Sam is unresolved.

## What was landed in code

- `CharacterImageIdentityPack`, `CharacterIdentityBuild`, `CharacterBodyCard`, `CharacterLoraDataset` and
  `CharacterLoraArtifact` now carry **`CharacterTemplateId`** (renamed through the language server, so every
  reference — including ~20 test call sites — was updated by the tool rather than by hand).
- The studio and the packs page **resolve** before they read or write identity: `_owner = await
  OwnerResolver.ResolveAsync(routeId)` then every identity call uses `IdentityKey` (the template). An unresolvable
  character shows the resolver's remedy (`…has no character template… Link it…`) instead of an empty page — Sam's
  page does exactly this today, by design.
- `CharacterStudio.razor`'s body card, builds, prompt/model overrides and pack reads all go through the resolved
  key.

**Deliberate scope decision, recorded:** the **physical column rename was NOT performed.** The C# property rename
already gave the compiler enforcement over every call site (which was the point), and doing DDL *plus* a data re-key
in one turn is the riskier combination. The legacy column *names* (`CharacterProfileId`) in the identity and
character-scoped tables now hold template ids. Deferred to **B-127 P3** as an isolated cosmetic migration; the plan
and backlog say so explicitly.

## Evidence

- **Studio resolves:** opening `/characters/f58f959a…` (the *Campground Intimacy* Becky **instance** id) now renders
  **"Campground Intimacy · 5 identity packs"** with v5 `Approved` — i.e. the instance id resolves to
  `de351eb3…` and finds the re-keyed packs. The LoRA-dataset link on that page is generated with
  `characterId=de351eb3…`, confirming the resolved key flows through the UI.
- **Data:** `SELECT CharacterProfileId, COUNT(*), MIN/MAX(Version), Approved` → `a4894571…` 9 (1…9, 1 approved);
  `de351eb3…` 5 (1…5, 1 approved). No version is outside 1..9 and no pack is negative.
- **Tests:** identity / studio / body-card / image-identity / reference / asset-tree / LoRA filter → **263 passed /
  0 failed**. Solution builds.
- App restarted on `data/dreamgenclone.dev.db` (Development), HTTP 200.

## Two bugs the run found (both fixed here)

1. **Re-key before renumbering** collided on `UNIQUE (CharacterProfileId, Version)`: two chains both start at v1, so
   moving one under the template hit the other's v1. The apply now renumbers *first* (while each chain is still
   grouped under its own key) and re-keys after. Caught by SQLite, and the transaction rolled back cleanly — the
   verification query confirmed 0 negative versions and the original owner keys intact before the fix.
2. **`CHECK (Version > 0)`** rules out the negative-shift trick the renumbering used for collision-free two-phase
   updates; it now shifts by a high positive offset (`+100000`) instead.

## Sam — left unlinked on purpose

Sam (`a9137ebf…`) is an asset-owned character with no link row, so nothing about it was guessed: its 2 builds, its
prompt override and its settings row still carry the asset id, and Sam's studio page says so with the remedy. The
command is idempotent: after the character is linked — through the app's *Link character identity* action (P0b), or
`--link a9137ebf…=<template id>` — a second `apply` moves those rows under the template. Nothing is lost meanwhile
because nothing was moved.

## Next

**P0b surfaces:** `/characters/{templateId}` canonical with legacy ids resolving, the studio showing *owner template
+ instance* (and the packs panel's "scoped to a scenario character" sentence corrected), `TemplatesPanel` listing a
template's instances plus the `Link this character to a template` action (which is also how Sam gets linked), and
`SceneAssetTreeService` stopping the by-name merge that can open the wrong instance. Then P2 (consumers) and P3
(close-out, including the deferred physical column rename).
