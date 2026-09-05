# B-109 UI Contract — Identity Mechanism Qualification And Selection

**Extends:** existing Model Manager identity fields (`ModelDetailsEditor.razor` "Character Identity
Conditioning" section) and the Production Studio identity surfaces already described in
[B-106's ui-contract.md](../B-106-production-studio-staged-workflow/ui-contract.md) §3–§4.

## 1. Model Manager — qualified identity cells

Today: one set of `IdentityMechanism`/`IdentityStrength`/`IdentityAdapterRef`/`IdentityClipVisionRef`
fields per `RegisteredModel`. This package adds a **list** of identity capability cells per model,
each independently enabled/qualified:

```
Character Identity Conditioning
  [+ Add Identity Cell]
  ┌─────────────────────────────────────────────────────────┐
  │ Mechanism: [IP-Adapter (Plus Face) ▾]  Status: Qualified │
  │ Adapter ref: PLUS FACE (portraits)   Strength: 0.8       │
  │ Supports multi-reference: [ ]                            │
  │ [Disable]  [Edit]                                        │
  ├─────────────────────────────────────────────────────────┤
  │ Mechanism: [PuLID ▾]                  Status: Qualified  │
  │ Adapter ref: pulid_v1.1.safetensors  Strength: 0.75      │
  │ Supports multi-reference: [ ]                            │
  │ [Disable]  [Edit]                                        │
  ├─────────────────────────────────────────────────────────┤
  │ Mechanism: [IP-Adapter FaceID ▾]      Status: Unqualified│
  │ No proof recorded — cannot be enabled for production.    │
  └─────────────────────────────────────────────────────────┘
```

- The mechanism dropdown offers every `SceneImageIdentityMechanism` value, but a cell whose
  mechanism has no recorded proof-gate decision shows `Status: Unqualified` and its `[Enable]`
  action is replaced with an explanatory note (FR9-009) — never a silently-clickable enable.
- `Supports multi-reference` is only shown, and only togglable, for mechanisms whose installed
  custom-node contract has been verified (FR9-012) — otherwise it's absent, not just unchecked.
- Multiple cells for the same checkpoint can be `Qualified` and enabled simultaneously — this is
  the concrete surface for "support different ones at the same time."

## 2. Character Identity page — angle-aware selection is invisible by design

No new control is added to `/characters/identity`. Angle-aware selection (method 1) operates
automatically at render time from the existing approved face views — the user's only visible
change is that a render now shows, in its audit/inspector view, which view was actually selected
and why (FR9-004), not a new manual control.

## 3. Production Studio — per-render mechanism selection

Extends the existing identity model selector already in the Studio (`_selectedProductionIdentityModelId`)
to a two-part choice when more than one qualified cell exists for the resolved model:

```
Character Identity (one-pass)
  Model: [bigLust_v16 — RunPod Serverless BigLust ▾]
  Identity mechanism: [PuLID ▾]     (only shown when >1 qualified cell exists for this model)
  ☑ Dean   ☑ Becky
```

- When exactly one qualified cell exists, the mechanism dropdown is omitted entirely (no
  meaningless single-option control).
- When the requested mechanism is unqualified or disabled, the render action is unavailable with
  the exact reason stated inline (FR9-008), never a silent fallback to a different mechanism.

## 4. Coexistence clarity with the B-106 Identity stage

Per FR9-014/015, the Studio must not group "T2I identity mechanism" and "Identity stage (pixel
edit)" under one exclusive choice. They render as clearly separate concerns:

- The Composition-stage "Character Identity (one-pass)" control (§3 above) is labelled exactly
  that — a Composition-time option.
- The B-106 Identity stage (§4 of its own ui-contract) is labelled as a distinct stage in the
  stepper, with its own parent-attempt selector, entirely independent of what mechanism (if any)
  was used at Composition time.
- A one-line note in the Identity stage's panel states: *"This stage runs independently of any
  Composition-time identity mechanism. You can use both."*

## 5. Audit / inspector display

Wherever a compiled identity request is inspected (Production Studio inspector, debug event
viewer), show:

- Resolved target angle and the exact `SceneImageReferenceFaceView` selected (method 1).
- The qualified mechanism and cell used.
- The ordered list of reference assets used (1 for a non-multi-reference cell, up to 3 for a
  multi-reference cell), each by asset id and checksum.

## 6. Empty, loading, and failure states

| Region | Empty | Failure |
|---|---|---|
| Model Manager cell list | "No identity cells configured for this model." | n/a |
| Mechanism dropdown (Studio) | n/a (omitted per §3) | "No qualified identity mechanism available for &lt;character&gt;" naming the character |
| Audit view | "No identity conditioning was used for this attempt." | n/a |

## 7. Accessibility

Inherits the existing Model Manager and Production Studio accessibility rules (P2-051). The cell
list uses a standard list/row pattern with `Edit`/`Disable` as labelled buttons, not icon-only
controls, since their consequence (disabling a production-qualified mechanism) is state-changing.
