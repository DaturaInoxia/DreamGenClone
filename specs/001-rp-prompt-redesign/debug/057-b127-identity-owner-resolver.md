# 057 — B-127 P0a (part 1): the identity owner resolver and the explicit link store

**Status:** Delivered (Web builds; 250 identity/reference/scene-asset tests + 14 new resolver tests green; app
restarted on the dev DB — the new `CharacterIdentityLinks` table is created at startup)
**Date:** 2026-09-22
**Items:** B-127 (new), tracked from `specs/Planning/B-127-character-identity-template-ownership/plan.md`

## Report

Character text definitions live in `Templates` (`TemplateType.Character`) and identity used to key on whichever
**scenario instance** the studio was opened with, so one character had several identities (Dean: 8 packs on one
instance, 1 on the other; Becky's second scenario: none) and the Asset Manager merged scenario characters **by
display name** before linking to one arbitrary id. The link that fixes this already existed in the data — every
scenario character carries `TemplateId` — and nothing read it.

## What was built (additive; no behaviour change yet, nothing re-keyed)

**One resolution path** — `ICharacterIdentityOwnerResolver` / `CharacterIdentityOwnerResolver`:

- a `TemplateType.Character` template id → the owner itself (a Location/Guidance template id is refused, naming its
  actual type);
- a scenario character id → its payload `TemplateId`, else an explicit link row;
- a `SceneAssets` character id → its explicit link row;
- anything else → refused, naming the remedy.
- `IdentifyAsync` answers "which namespace is this id?" from the same private candidate lookup, so the resolver and
  the link action can never disagree; `ListInstancesAsync` lists one template's scenario instances.
- **No name matching, by construction**: the resolver's source contains no `.Name ==`, no `Name, StringComparison`
  and no case-variant comparison before the first id lookup, and a behavioural test adds a second template *also
  called "Becky"* plus a nameless scenario character and asserts the resolver still refuses. A name may label an
  owner; it may never choose one.

**One write path** — `ICharacterIdentityOwnerLinkService` + `CharacterIdentityLinkRepository` (new table
`CharacterIdentityLinks`, keyed by instance id, carrying `LinkedBy`/`LinkedUtc`):

- refuses an id that is not a character at all (asks the resolver), so a typo cannot create an orphan link;
- refuses a target that is not a `TemplateType.Character` template, naming its type;
- refuses to re-point an existing link at a different template unless `replaceExisting: true` — re-pointing an
  identity is deliberate, never a second click's side effect.

**Deliberate change from the plan:** the link lives in its own table rather than in a new
`SceneAssets.CharacterTemplateId` column. A scenario character can be linked with no asset existing, and the link
needs who/when — and the new table keeps `SceneAssetRepository` (four SELECTs, an ordinal read and an INSERT) out of
the change entirely. The plan has been updated to match.

Registered in `Program.cs` (link store singleton, resolver + link service scoped) and ensured at startup beside the
body-card and build stores.

## Evidence

- `CharacterIdentityOwnerResolverTests` (new, 14): template/scenario-character/asset-character resolution; both
  scenario instances of Becky resolve to the SAME template key while keeping distinct instance ids; an asset
  character with no link refuses and names the link action; a nameless scenario character refuses; a
  non-character template refuses naming `Location`; an unknown id refuses; `IdentifyAsync` for all three kinds plus
  null; `ListInstancesAsync` returns both instances; the link action refuses a non-character template and an
  unknown id (writing nothing), and refuses a re-point without `replaceExisting`.
- Wider run: identity / CharacterStudio / body-card / image-identity / reference / asset-tree filter → **250 passed
  / 0 failed**; repository suite → 18 passed.
- App restarted (`Development`, `data/dreamgenclone.dev.db`, HTTP 200) and `CharacterIdentityLinks` verified present
  in the dev DB — which also proves the new registrations resolve at startup.

**Defect found and fixed while running the wider filter:** `Supersede_RefusesACanonicalFullBodyPointerThatIsNotInThePack`
(debug 055) had an impossible setup — it tried to *approve* a pack whose canonical pointer named a foreign asset,
which approval correctly refuses, so the test never reached the supersede guard it was written for. It now writes
that corrupted row directly (the store refuses it through every API path) and then asserts supersede refuses to
carry the broken pointer forward without retiring the pack.

## Next (in order, and why that order)

1. **The key rename + the migration are ONE step.** Renaming
   `CharacterProfileId` → `CharacterTemplateId` without re-keying the rows would make the column name lie, and
   switching resolution to the template *before* the rows are re-keyed would hide Dean's 8 existing packs. So the
   next deliverable is `b127-identity-rekey` (`--preview` / `--apply`, renumber by `CreatedUtc` per template, refuse
   to leave two approved packs) **together with** the domain/repository rename, executed on a backed-up dev DB.
2. **P0b** — the surfaces: `/characters/{templateId}` canonical with legacy ids resolving, the studio showing owner
   template + instance, `TemplatesPanel` listing a template's instances with the `Link this character to a
   template` action, and `SceneAssetTreeService` no longer merging by name.
3. **P2/P3** — consumers (scene rendering, composition, edit workspace, LoRA) resolve through the resolver; then the
   grep proofs and deletion of the legacy per-instance path.
