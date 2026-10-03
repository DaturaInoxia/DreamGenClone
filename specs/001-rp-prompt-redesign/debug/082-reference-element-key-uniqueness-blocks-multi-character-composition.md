# 082 — A multi-character Composition refuses to queue: "Reference application element keys must be unique."

**Session:** 2026-10-02 · **State:** implemented, build + targeted tests green
**Scope:** the reference-binding validator's uniqueness rule, plus the two request members another
in-flight change was missing (see Resolution part B).
**Related:** 081 (the same page's face/build pickers), B-130.

---

## Report

Operator, immediately after 081 was verified on the Composition Composer (same session/production):

> "Reference application element keys must be unique."

Thrown while queueing a Composition, not while binding.

---

## Analysis

`SceneImageService.SerializeReferenceApplications` → `ReferenceApplicationSelectionValidation.Validate`
required the **element key alone** to be unique:

```csharp
if (applications.Select(application => application.ElementKey.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != applications.Count)
    throw new InvalidOperationException($"{label} application element keys must be unique.");
```

But `ElementKey` is the LEGACY LABEL of an element (`ReferenceSlotPlanner.ElementKeyFor`): a face reports
`"Identity"`, a build `"Body"`, whoever they belong to. It is used as a strategy-lookup key
(`ReferenceStrategyCatalogue.ByElementKey`) and as the fallback slot-kind map
(`ImageStepBindingAxes.SlotKindFor`, `ReferenceBindingPromptRemoval.TryResolveSlotKind`) — it is **not an
address**. The address of a binding is the element **and** the character it belongs to, which is exactly
how every other surface matches a binding to its slot (`ImageStepComposer.BindingFor` matches on
`Kind + ActorKey`) and how a prompt scope is written
(`StepPromptElementPlan.ScopesFor(slotKind, binding.ActorKey)` → `character:{key}.appearance`).

So the rule forbade the normal shape of a two-character composition: Becky's face and build plus Dean's
face and build is four bindings whose element keys are `Identity, Body, Identity, Body`. Reported this
time because 081 made the per-character Face slot exist at all, so binding both characters became the
ordinary path.

The rule also contradicted the planner, which is the ONLY producer of these bindings: in
`ReferenceSlotPlanner.Plan` a second assignment to one slot is refused unless the blueprint declares
`AllowsMultiple` —

> `Slot 'Body' for actor 'x' was filled more than once for one step.`

— so "one image per single-valued element" was already owned, with a better message, where the blueprint
is actually known. The validator cannot see `AllowsMultiple`, which is why its version of the rule had to
be wrong for either multi-character frames or multi-image elements (a wardrobe's dress and shoes).

---

## Plan

1. Make the validator's uniqueness rule the binding's **address**: element + character + position.
2. Pin the reported case with a test that runs the REAL blueprint → REAL planner → validator, and pin the
   two shapes the old rule broke (two characters; two images on one multi element). The retargeted existing
   test keeps refusing a genuine duplicate at one address.
3. Restore the build, which a concurrent in-flight change had left broken (Resolution part B), then run the
   RolePlay suite and restart the app.

---

## Resolution

### A. The rule (`ReferenceApplicationSelectionValidation`)

```csharp
var address = string.Join('|', ElementKey, ActorKey, Ordinal);
if (!addresses.Add(address))
    throw ... "element keys must be unique per character and position: '<key>' is declared twice with the
                same character and ordinal, so two references would occupy one address."
```

A binding without an actor and without an ordinal still collides with its twin, so the original refusal is
preserved for the shape it was written for; a per-character element (once per character) and a multi-image
element (once per position) are now allowed.

| File | Change |
|---|---|
| `DreamGenClone.Web/Application/RolePlay/ReferenceApplicationSelectionValidation.cs` | Uniqueness is now the address (element + actor + ordinal), with the reasoning recorded in the comment. |
| `DreamGenClone.Tests/RolePlay/ReferenceApplicationSelectionValidationTests.cs` | `Validate_WithAnElementKeyDeclaredTwiceAtOneAddress_Refuses` (retargeted, same guarantee), `Validate_WithTheSameElementForTwoCharacters_Accepts`, `Validate_WithTwoImagesOnOneMultiElement_Accepts`, `Validate_WithTheCompositionPlannersTwoCharacterBindings_Accepts` (the reported case end to end through the real blueprint and planner). |

### B. Restoring the build (approved by the operator)

The working tree did not compile — a concurrent in-flight change (scene LoRAs on the edit path) had added
call sites and a test but not the members they call:

```
SceneImageService.cs(1036): 'SceneImageEditRequest' has no 'SceneLoras'
SceneImageService.cs(1496): 'MediaEditRunRequest' has no parameter 'SceneLorasJson'
SceneAssetImageEditCompilationService.cs(260): 'EnqueueSceneAssetImageEditRequest' has no 'SceneLoras'
SceneAssetImageEditCompilationService.cs(260): 'MediaEditRunRequest' has no parameter 'SceneLorasJson'
```

Three members added, no behaviour invented: `SceneImageEditRequest.SceneLoras`,
`EnqueueSceneAssetImageEditRequest.SceneLoras` (both `IReadOnlyList<SceneImageLoraSelection>?`), and
`MediaEditRunRequest.SceneLorasJson` (appended so no existing positional construction shifts). The
remaining thread of that change — reading the stack in the edit job handler and forwarding it from
`MediaEditCompilationService.EnqueueRunAsync` onto `MediaEditImageEditingJobPayload` — is deliberately
NOT touched here: it is that change's to finish, and guessing its wiring would collide with it.

---

## Evidence

- `dotnet build DreamGenClone.Web/DreamGenClone.csproj` — **0 errors**. `DreamGenClone.Tests` — **0 errors**.
- `ReferenceApplicationSelectionValidationTests` — **19/19 passed**, including the four above by name;
  `Validate_WithTheCompositionPlannersTwoCharacterBindings_Accepts` is the reported case.
- `ReferenceSlotPlannerTests` + `SceneAssetImageEditRunCutoverTests` + the validation suite — **47/47**.
- Full RolePlay area: **3397 passed / 4 failed (3401)** — the same four pre-existing failures from other
  in-flight worktree work reported in 081 (`SdxlSceneImagePromptBuilderTests` × 3,
  `SceneLoraSelectionWireTests.Read_WithAMalformedStack_Throws`), none of them from this change.
- App restarted from `DreamGenClone.Web` with `ASPNETCORE_ENVIRONMENT=Development`; `http://localhost:5177`
  answers **HTTP 200**.

---

## Validated

- [ ] pending — operator to confirm a Composition with two characters' face and build bindings queues
      without the element-key refusal.
