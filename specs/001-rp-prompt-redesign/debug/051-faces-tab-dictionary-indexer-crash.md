# 051 — Faces tab crashed: Panel C bound its inputs to a dictionary indexer

**Status:** Fixed (build + 30 focused tests green; Faces tab verified rendering in the browser; 0 exceptions since restart)
**Date:** 2026-09-21
**Reported:** "the ui is crashing throwing exceptions (Reload Link) when clicking the faces tab characters/a9137ebfa4df43d08c3347242aaa2261"

## Report

Clicking the **Faces** section of the Character Studio for character
`a9137ebfa4df43d08c3347242aaa2261` killed the interactive circuit and the page fell back to Blazor's
"You can reload the page" error UI. From the app log:

```
2026-09-21 22:02:14.937 [WRN] Unhandled exception rendering component: The given key 'ProfileLeft' was not present in the dictionary.
System.Collections.Generic.KeyNotFoundException: The given key 'ProfileLeft' was not present in the dictionary.
   at System.Collections.Generic.Dictionary`2.get_Item(TKey key)
   at DreamGenClone.Components.Pages.CharacterStudio.BuildRenderTree(RenderTreeBuilder __builder) in CharacterStudio.razor:line 476
2026-09-21 22:02:14.941 [ERR] Unhandled exception in circuit 'NIAPfFhYRgBHFl5D0pwT__Idv0tnCSBW0aNSX-nByKk'.
```

## Analysis

`CharacterStudio.razor:478` rendered the profile-direction confirmation with **`@bind="_profileConfirmations[view]"`**.
`@bind` on a dictionary indexer reads the key *while rendering*, and `_profileConfirmations` was declared as an
empty dictionary that **no code ever populates** — so the read threw `KeyNotFoundException` and took the circuit
with it. The reading is the crash; nothing was wrong with the data.

Why it fired only now: item 3 (B-121 Phase F/G, `debug/049`) widened the guard from

```razor
view is ProfileLeft or ProfileRight && angle?.Status == Pending      // effectively unreachable: upload sets Complete
```
to
```razor
angle?.ManualConfirmationRequired == true && angle.Status != Accepted  // every profile card with a record
```

so the confirmation control — and with it the indexer read — became reachable on a real character. DB state for
this build (`c6dd9a6c…`, table `CharacterIdentityAngles`): view 3 (`ProfileLeft`) has
`ManualConfirmationRequired = 1` with `Status = 1` (NotStarted) and **`OutputArtifactId` NULL**, i.e. a record the
older code never rendered the control for.

**Same latent fault, newly reachable:** `:442`/`:443` bound `@bind="_overrideReasons[attempt.Id]"` and
`@bind="_overrideAuthors[attempt.Id]"`, rendered whenever an attempt is `Failed`. The direction gate makes
`Failed` routine now (a wrong-facing render is marked `Failed`), so that crash was one failed attempt away.

This is a B-121 UI-contract fault, not an engine fault: a per-view input must never be bound to an indexer whose
key may not exist yet.

## Plan

Files: `CharacterStudio.razor` (Panel C) + the Faces contract tests + this record. No service, repository,
persistence or DB change; the values handed to the services stay identical.

1. Replace all three indexer bindings with explicit reads/writes: `checked=`/`value=` plus handlers, reads via
   `TryGetValue` (`ProfileConfirmed`, `OverrideReason`, `OverrideAuthor`), writes via the indexer
   (`SetProfileConfirmed`, `SetOverrideReason`, `SetOverrideAuthor`).
2. Offer the profile confirmation only when there is something to confirm
   (`!string.IsNullOrWhiteSpace(angle.OutputArtifactId)`), so an image-less profile record shows no control.
3. Contract tests: no indexer binding on those three dictionaries (comments excluded) + the confirmation
   condition keeps its `ManualConfirmationRequired`/not-`Accepted`/has-output shape.
4. Build, run the affected tests, verify the Faces tab renders for this character, restart the app.

## Resolution

- `CharacterStudio.razor`: the three bindings replaced with the helpers above; the confirmation condition is now
  `ManualConfirmationRequired == true && Status != Accepted && !string.IsNullOrWhiteSpace(OutputArtifactId)`.
  Helpers documented at the field declarations, naming this incident so the pattern is not reintroduced.
- `CharacterStudioFacesContractTests`: `PanelC_NeverBindsItsPerViewInputsToADictionaryIndexer` (fails if any of
  the three `@bind="_dict[` forms returns; asserts the tolerant `TryGetValue` readers) and the profile-confirmation
  test now also requires the output-artifact guard. A `Markup` view of the component (line comments stripped)
  backs the markup assertions, because the helper's own doc comment quotes the offending syntax.
- Verified: `CharacterStudioFacesContractTests|CharacterIdentityPromotionGateTests|CharacterIdentityAngleYawGateTests`
  → **30 passed / 0 failed**; browser: Faces tab renders Panel A/B/C including the Profile-left card, with
  `.blazor-error-ui` absent and no "Reload" text; app log shows **0** `Unhandled exception` / `KeyNotFoundException`
  since the restart (dev DB, Development, `:5177`).

## Note for future work

Any per-item UI state (per view, per attempt) is read through a tolerant helper. `@bind` is only for plain
fields; a dictionary keyed by a view or attempt id is written through an explicit setter, because the render
pass must never depend on the user having touched that control first.
