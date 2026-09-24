# B-127 — Character identity ownership: key identity to the Character template

**Status:** `planned` — plan written 2026-09-22; direction chosen by the operator the same day. **P0a part 1
DELIVERED 2026-09-22** (resolver + explicit link store; `debug/057`). The key rename and the data re-key are the
next step and are deliberately ONE change (see §5).
**Related:** B-121 (identity studio, build/promotion machinery), B-122 (body-complete pack — Phase 0 keys on the same
owner), B-123 (LoRA — needs ONE identity per character), B-124 (reference model + Asset Manager shell — the same
surface), `specs/Planning/character-identity-ownership-and-linking.md` (the 2026-09-16 decision record this
implements, with the owner *entity* replaced by the template as the owner key), `specs/Planning/identity-lora-program-map.md`.

## 1. The problem, verified in the dev DB (2026-09-22)

Character definitions and character identity live in two namespaces that never meet:

| | Stored in | Key |
|---|---|---|
| Text definition — the `/profiles?tab=templates` surface (`TemplatesPanel`) | `Templates`, `TemplateType='Character'` | `de351eb3…` Becky, `a4894571…` Dean, `51923303…` Ken, `4684be8f…` Sam |
| Scenario character — what RP sessions and the Character Studio use | `Scenarios.PayloadJson.Characters[]` | `f58f959a…` Becky@Campground, `4a3e7417…` Becky@The Party, `faee1ec0…` Dean@Campground, `a9e0782a…` Dean@The Party |
| Identity (packs, builds, body card, views, LoRA datasets) | `CharacterImageIdentityPacks.CharacterProfileId`, `CharacterIdentityBuilds.CharacterProfileId`, `CharacterBodyCards.CharacterProfileId`, `CharacterLoraDatasets.CharacterProfileId` | **whichever id the studio was opened with** — normally the scenario character id |

**The template link already exists in the data and nothing reads it:** every scenario character carries
`"TemplateId"` in the scenario payload (`f58f959a… → de351eb3…`, verified by query). Identity resolution never
consults it.

**Consequences, measured:**

| Owner (current identity key) | Is | Template | Packs | Builds | Body cards |
|---|---|---|---|---|---|
| `faee1ec0…` | Dean @ Campground Intimacy | `a4894571…` | 8 (v8 approved) | 1 | 0 |
| `a9e0782a…` | Dean @ The Party | `a4894571…` | 1 | 0 | 0 |
| `f58f959a…` | Becky @ Campground Intimacy | `de351eb3…` | 5 (v5 approved) | 1 | 0 |
| `4a3e7417…` | Becky @ The Party | `de351eb3…` | **0** | 0 | 0 |
| `a9137ebf…` | **Sam, a `SceneAssets` row of `Type='Character'`** | **none** (the template `4684be8f…` "Sam" is a different, unrelated id) | 0 | 2 | 0 |

1. **One character, many identities.** Dean's face history is 8 packs on one instance and 1 on the other; Becky's
   second scenario has nothing. Identity work is not portable, and it silently *looks* like it is.
2. **The Asset Manager already merges by name and then links to one id.**
   `SceneAssetTreeService.BuildTreeAsync` groups scenario characters into a case-insensitive `charactersByName`
   dictionary and sets `Href = /characters/{characterIds[0]}` — so two Beckys collapse into one row that can open the
   instance holding zero packs. `character-identity-ownership-and-linking.md` forbids exactly this ("never match by
   display name alone").
3. **Templates and personas have no identity path at all.** A session's player persona is
   `RolePlaySession.PersonaTemplateId` — a *template* id — and no resolution step maps a template to a pack.
   `Templates.ImagePath` (+ `TemplateImageEditor`) is a third, unconnected home for character images.
4. **B-123 cannot be built on this.** A LoRA is one identity for one character; it cannot be "the packs of whichever
   scenario instance you opened".

## 2. Decision (operator, 2026-09-22)

**Identity is owned by the Character template.** A scenario character, an asset character and a persona are
*projections* of one character definition, and all of them resolve to the same identity.

Rejected alternatives, recorded: a canonical `CharacterIdentityOwner` entity linking namespaces (the 2026-09-16
recommendation — more machinery for the same outcome, and the template already is the definition), and link tables
only (every reader must then disambiguate several packs for one character — the outcome the owner was meant to
prevent).

## 3. Resolution — one path, no fallback

New `ICharacterIdentityOwnerResolver` (`DreamGenClone.Web/Application/RolePlay/`):

```text
ResolveAsync(string anyOwnerId) -> string characterTemplateId
```

- `anyOwnerId` is a **character template id** → itself.
- `anyOwnerId` is a **scenario character id** → that character's `TemplateId` from the scenario store.
- `anyOwnerId` is a **character asset id** (`SceneAssets.Type='Character'`) → its `SceneAssets.CharacterTemplateId`.
- Anything else → **throw**, naming the id and the fix: *"…has no character template. Create or link one in
  Templates, then reopen the studio."*

Rules this resolver obeys, all of them from the decision record: never match by display name, never merge silently,
never invent a template. There is exactly one resolution function; no caller gets to guess.

**Data model:**

| Change | Detail |
|---|---|
| Identity key column renamed | `CharacterImageIdentityPacks.CharacterProfileId` → `CharacterTemplateId`; same for `CharacterIdentityBuilds`, `CharacterBodyCards`, `CharacterLoraDatasets`. SQLite `ALTER TABLE … RENAME COLUMN`, so the compiler forces every call site to be revisited rather than leaving a silently-reinterpreted column |
| Domain property renamed | `CharacterImageIdentityPack.CharacterProfileId` → `.CharacterTemplateId` (and the build/body-card/dataset equivalents), so no code can confuse "the instance" with "the owner" |
| `SceneAssets.CharacterTemplateId` **added** | **Revised 2026-09-22 (`debug/057`):** the link lives in its own table, `CharacterIdentityLinks (OwnerInstanceId PK, CharacterTemplateId, LinkedBy, LinkedUtc)`, instead of a new `SceneAssets` column. A scenario character can be linked with no asset existing, the link needs who/when, and this keeps `SceneAssetRepository` (four SELECTs + an ordinal read + an INSERT) out of the change. `SceneAssets.CharacterProfileId` **stays** as the *origin instance* provenance it holds today |
| Pack versioning | `UNIQUE (CharacterProfileId, Version)` is re-keyed to `(CharacterTemplateId, Version)`. Merging two per-instance chains therefore **requires renumbering** (both start at v1) — see §4 |
| No new store | Packs, assets, builds, cards, datasets all keep their tables; only the owner key's meaning and name change |

## 4. Migration — explicit, previewed, user-confirmed

Required, not optional: the dev DB already holds 14 packs, 4 builds and a third namespace (Sam) that must be
resolved by a human. Runs as a DbQuery command (`b127-identity-rekey`), never as an app side effect:

- `--preview` prints the whole plan: every owner id, its resolved template (or `UNRESOLVED`), the packs to re-key,
  the new version numbers, `SupersedesId` links that would be broken, and any template that would end up with more
  than one `Approved` pack.
- `--apply` performs it in one transaction: re-key, renumber by `CreatedUtc` per template, keep a `SupersedesId` only
  when both endpoints land in the same template (otherwise clear it and record the drop in the report), backfill
  `SceneAssets.CharacterTemplateId` from each asset's instance, and print row counts.
- **One approved pack per template** is enforced at the end: if a merge would leave two, the command refuses and
  names them — the user decides which to supersede (never "pick the newest" automatically).
- The dev DB is backed up first (`artifacts/tmp/db-backups/`), and the migration is re-runnable and idempotent.

**Open for the operator: Sam.** The asset-owned character `a9137ebf…` (2 builds) has no template, and the unrelated
template named "Sam" (`4684be8f…`) must not be chosen by name. The studio/template UI gets an explicit
*Link this character to a template* action (a picker, showing ids), and until it is used the character reports
itself as unlinked.

## 5. Phases

| Phase | Scope | State |
|---|---|---|
| **P0a part 1 — resolver + link store** | `ICharacterIdentityOwnerResolver`/`CharacterIdentityOwnerResolver` (three namespaces, refusals, `IdentifyAsync`, `ListInstancesAsync`, no name matching); `ICharacterIdentityOwnerLinkService` + `CharacterIdentityLinkRepository` (own table, `LinkedBy`/`LinkedUtc`, refuses a non-character template, an unknown id, and a re-point without `replaceExisting`); DI + startup schema | **DELIVERED 2026-09-22** (`debug/057`; 14 tests, wider filter 250 green, table verified in the dev DB) |
| **P0a part 2 — the key + the data** | Rename the identity key on the domain types (`CharacterImageIdentityPack` / `CharacterIdentityBuild` / `CharacterBodyCard` / `CharacterLoraDataset` / `CharacterLoraArtifact` → `CharacterTemplateId`) and resolve before every identity read/write in the studio and the packs page; `b127-identity-rekey apply` re-keys the values (packs + builds + body cards + LoRA + character-scoped overrides/settings), renumbers by `CreatedUtc`, and resolves the flagged shapes | **DELIVERED + APPLIED 2026-09-22** (`debug/059`): Dean 9 packs v1…v9 (one approved), Becky 5 (one approved), 16 rows re-keyed, 28 version sets, 1 shadowed draft demoted; 263 tests green; studio shown resolving an instance id to the template's packs. **The physical column rename was deferred to P3** (the C# rename already forced every call site; DDL + a data re-key in one turn is riskier). Sam left unlinked on purpose |
| **P0 — key + resolution (code only, no data)** | Resolver; rename the identity key in domain + repositories + schema/ALTER guards; every resolution site passes through the resolver; studio and Asset Manager show the owner template + the instance; **stop the name merge** in `SceneAssetTreeService` (one root per template, instances listed inside); template view lists its instances and their identity status; body service and promotion take the template key | `ICharacterIdentityOwnerResolver.cs`/`CharacterIdentityOwnerResolver.cs` (new); `CharacterImageIdentityRepository.cs`, `CharacterIdentityBuildRepository.cs`, `CharacterBodyCardRepository.cs`, `CharacterLoraRepository.cs`; `CharacterImageIdentityModels.cs`, `CharacterIdentityBuildModels.cs`, `CharacterBodyCardModels.cs`, `CharacterLoraModels.cs`; `CharacterImageIdentityService.cs`, `CharacterIdentityBuildService.cs`, `CharacterIdentityBodyService.cs`, `CharacterIdentityPromotionService.cs`, `CharacterIdentityFrontService.cs`/`Angles`/`Garment` (build profile ids), `SceneAssetProfilePackJobHandler.cs`, `ReferenceBootstrapService.cs`, `SceneAssetRepository.cs` (+`SceneAssets.CharacterTemplateId`), `SceneAssetTreeService.cs`, `CharacterStudio.razor`, `CharacterIdentity.razor`, `AssetStudio.razor`, `TemplatesPanel.razor` |
| **P0b — the surfaces** | `/characters/{templateId}` canonical with legacy redirect; studio shows owner template + instance; `TemplatesPanel` lists a template's instances and their identity status with the `Link this character to a template` action; `SceneAssetTreeService` stops merging by name (one root per template, instances inside); `AssetStudio` owner filter | **DELIVERED 2026-09-22** (`debug/060` surfaces, `debug/061` link action): canonical route + redirect, owner line in the studio header, page-level remedy on every tab, resolver-first packs page, `SceneAssetTreeService` grouping by **resolved owner** (unlinked characters marked, never dropped), and the **Character identity panel in `TemplatesPanel`** — instances of the template, the unlinked work list from `ICharacterIdentityOwnerResolver.ListUnlinkedAsync()`, *Link character identity* / *Remove link*. **Sam linked through the UI** and his rows re-keyed (`20 rows re-keyed`); 235 tests green. Remainder inside this phase: a character *asset* still gets its own asset-manager root instead of nesting under its owner (its images hang off the asset id) — folded into P2 |
| **P1 — migration** | *(merged into P0a part 2 — the rename and the re-key are one change)* | with P0a part 2 |
| **P2 — consumers** | Scene rendering, composition, image editing and LoRA resolve identity through the resolver; the identity pickers list packs per template; the asset tree's owner filter uses the template owner | not started |
| **P3 — close-out** | Grep proofs that no name-matching and no second resolution path remain; delete the legacy per-instance path; **the deferred PHYSICAL column rename** (`CharacterProfileId` → `CharacterTemplateId` on the identity and character-scoped tables, whose values are already template ids); record diagnostics for both tabs | not started |

## 6. Tests / verification

- **Resolver:** each of the three owner kinds resolves to the same template id; a blank/unknown id and an
  asset-owned character with no template each refuse with the message naming the fix; **no** name-based resolution
  is possible (contract test: the resolver source contains no name/`DisplayName` comparison).
- **Key:** a pack created for a scenario character is listed by its template id and by every instance of that
  template; `Supersede`/`GetLatestApprovedPackAsync` operate per template; a second `Approved` pack for one template
  is refused.
- **Schema:** rename + `SceneAssets.CharacterTemplateId` are additive/idempotent on an existing DB (ALTER guards),
  and the new UNIQUE is verified by a test that inserts two v1 packs under different templates.
- **UI contracts:** the Asset Manager emits one root per owner template (no case-insensitive name dictionary); the
  studio shows the owner template id and the instance it was opened from.
- **Migration:** `--preview` output is asserted against a seeded fixture (re-key + renumber + one-approved rule),
  and the re-run is a no-op.

## 7. Decisions — resolved by the operator 2026-09-22

1. **Sam** (asset-owned, 2 builds): adopt the explicit **`Link this character to a template`** action, and link Sam
   by hand through it. Until it is used, the character reports itself as unlinked and identity work is refused — no
   automatic creation and no name matching.
2. **Merged version numbering:** **renumber by `CreatedUtc` per template** (Dean becomes one v1…v9 chain). Each
   original chain's numbering and the `SupersedesId` links that had to be cleared are recorded in the migration
   report.
3. **Studio route:** **`/characters/{templateId}` is canonical, and legacy ids resolve to it** (a scenario
   character id or an asset id redirects to the template URL), so existing links and bookmarks keep working while
   there is exactly one canonical address per character.

## 7a. Sequence inside P0 (so the app builds at every step)

P0 is one coherent change but it cannot be a single commit — the key rename makes every call site fail to compile
until it is visited. It is executed in two sub-phases, each of which builds and passes tests on its own:

- **P0a — the resolver and the key, no UI change.** `ICharacterIdentityOwnerResolver`; rename the identity key in
domain + repositories + schema guards; the explicit link store (own table, see §3); every *service* resolution site
routed through the resolver (front/angles/garment/body/promotion/build services, profile-pack job, reference
bootstrap); the studio keeps accepting its current id and resolves it. Tests: resolver (three owner kinds +
refusals + no name-matching contract), per-template pack listing/approval, schema rename idempotency.
  **Part 1 (resolver + link store) is delivered** (`debug/057`); part 2 (key rename **with** the re-key) is next.
- **P0b — the surfaces.** `/characters/{templateId}` canonical with legacy redirect; studio shows owner template +
instance; `TemplatesPanel` lists a template's instances and their identity status with the
`Link this character to a template` action; `SceneAssetTreeService` stops merging by name (one root per template,
instances inside); `AssetStudio` owner filter uses `CharacterTemplateId`.

P1 (migration) follows P0, then P2 (consumers), then P3 (close-out).


## 8. Non-goals

- No name matching, ever — including "obvious" cases.
- No silent merge of two instances' packs, and no automatic template creation from a character's name.
- No change to what a pack *contains* (B-121/B-122 own that); this item only changes what a pack *belongs to*.
- B-122 Phase 0's remaining work (E-2 view grids, E-3 promotion panel/settings, F live build) continues on the
  template-keyed path — it is not replaced by this item, it is re-keyed by it.
