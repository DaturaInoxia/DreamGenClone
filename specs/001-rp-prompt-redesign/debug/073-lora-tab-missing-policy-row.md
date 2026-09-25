# 073 — LoRA images tab killed the circuit: seeded store opened without its row

**Reported:** 2026-09-25 (operator): "runtime error when LoRA images tab is selected".
**Feature:** B-123 Phase 1, `LoraDatasetWorkspace.razor` (new this session).

## Report

Selecting **LoRA images** on a character with an approved body-complete pack killed the Blazor circuit. Log:

```
01:08:48 [WRN] Unhandled exception rendering component: Missing required LoRA curation policy row 'global'.
              No global row exists for this profile, and no threshold is assumed in code.
01:08:48 [ERR] Unhandled exception in circuit 'JYpKffExAJ3EJth5LvOoX2bVib8FtKraArDviPCgoQI'.
              System.InvalidOperationException: Missing required LoRA curation policy row 'global'.
01:08:48 [WRN] Unhandled exception rendering component: Cannot access a disposed object.   ← the circuit tearing down
```

## Analysis

**Two independent defects, one cause each.**

1. **The seed never ran.** `CharacterLoraRepository.OpenAsync` created the
   `CharacterLoraCurationPolicies` **table** (it runs `SchemaSql` on every open), but the seeded global row was
   written only by `EnsureSchemaAsync` — and the app never calls it. Its one call site is
   `ProductionMediaCompilers.cs:567`, a cold path. So the store was left in a half-migrated state: table present,
   row absent.
   *How this was missed:* the sibling `ImageWorkflowRepository` is safe because its **private** `EnsureSchemaAsync`
   (schema + seed) is invoked from `OpenAsync`, so any read seeds. I copied the seed into the LoRA repository's
   **public** `EnsureSchemaAsync` and assumed something called it, without checking. `grep EnsureSchemaAsync`
   across the Web project showed only `MediaEditRepository`, `ICharacterBodyCardRepository`,
   `ICharacterIdentityLinkRepository` and `ICharacterIdentityBuildRepository` — never the LoRA store.

2. **The component let a data failure escape into the circuit.** `LoadAsync` resolved the policy outside any
   try/catch, so the exception propagated out of the render and tore the circuit down — the operator saw a blank
   page instead of the reason. A guarded failure and a silent one look identical to the operator when the guard is
   a crash.

## Plan

1. Seed the policy row on **every open**, so the schema and the data that makes it usable always arrive together —
   the asymmetry that caused this. `EnsureSchemaAsync` becomes an explicit "make the store ready" entry point that
   simply opens.
2. Guard the component's load and put the failure on screen in the service's own words, with its own view (not
   inside the workspace, which would also render the unrelated "no body-complete pack exists" message).
3. Regression tests: resolving the policy after an ordinary *read* (no explicit ensure) must work; a policy row whose
   payload is incomplete must stop the plan and name the policy type.

## Resolution

- `DreamGenClone.Infrastructure/RolePlay/CharacterLoraRepository.cs`
  - new `SeedCurationPolicyRowAsync(connection, ct)` — idempotent `INSERT OR IGNORE`, called from `OpenAsync`.
  - `EnsureSchemaAsync` now opens the store (and therefore seeds) rather than carrying its own copy of the seeding.
- `DreamGenClone.Web/Components/Editing/LoraDatasetWorkspace.razor`
  - `LoadAsync` wraps everything after the owner resolve; sets `_loadFailed` + `_error`.
  - a `_loadFailed` view renders the reason instead of the workspace.
- `DreamGenClone.Tests/RolePlay/CharacterLoraCoveragePlanGeneratorTests.cs`
  - `Policy_IsSeededByTheFirstOpen_NotOnlyByAnExplicitEnsure` (would have caught this)
  - `Generate_WorksOnAFreshStore_WithNoExplicitEnsure`
  - `Generate_RefusesToPlanWhenThePolicyRowIsIncomplete` (replaces the missing-row test, whose premise the
    self-seeding fix makes unreachable)
  - `Policy_CharacterRowOverridesGlobal` / `Policy_ResetRestoresTheSeedAndRestartKeepsTheEdit` unchanged.

## Validated

- [ ] pending — operator restarts the app and opens the LoRA images tab.
- Evidence so far: 123 focused tests green (schema, seed, generator, composer, workspace contract, workflow store);
  `DreamGenClone.Web` builds with 0 errors. The dev DB has no row yet because the **running app is the pre-fix
  build**; `OpenAsync` writes it on the first touch after a restart. Confirm with
  `helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/b123-lora-cell-templates.sql` and a direct read of
  `CharacterLoraCurationPolicies`.

## Lesson (repo memory)

A new repository that seeds data must seed it where **every open** passes, not only in an `EnsureSchemaAsync` that
something else is assumed to call. A table that exists without its seeded row is a half-migrated store, and the
first read of it fails in the loudest possible place.
