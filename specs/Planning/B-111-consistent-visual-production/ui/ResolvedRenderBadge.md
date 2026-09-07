# ResolvedRenderBadge

## 1. Job (U1)
Expose resolved render submission settings.

## 2. Reused by
- Studio: Composition.
- Studio: Identity.
- Studio: Finish.
- Bootstrap.
- Asset Manager.

## 3. Input contract
All types below are proposed design types, not final APIs.

- `ResolvedRenderSettings Settings` - exact resolved values for the pending or completed render.
- `RenderLifecycleState State` - backend state of the render.
- `bool IsExpandable` - whether the host permits the full submitted-payload view.
- `RenderIdentifier RenderId` - optional render identity for completed output.
- `IReadOnlyList<RenderWarning> Warnings` - explicit warnings about stale or incomplete resolution.

## 4. Output/events
- `EventCallback<RenderPayloadViewRequestedEventArgs> OnViewSubmitted` - opens the exact payload view.
- `EventCallback<RenderBadgeExpandedEventArgs> OnExpanded` - reports compact/full display state.
- `EventCallback<RenderOpenedEventArgs> OnOpenRender` - opens the completed render when available.

## 5. States
- `empty`: settings have not been resolved; show why and the required prerequisite.
- `loading`: settings are being resolved.
- `error`: resolution failed; show the diagnostic and do not invent values.
- `populated`: compact resolved settings are available.
- `draft`: values are still editable upstream.
- `stale`: settings no longer match the selected source or strategy.
- `submitted`: exact values have been handed to the backend.
- `complete` or `failed`: terminal render state.

## 6. Data-volume behaviour (U4)
Keep the badge compact and never inline the full prompt, payload, or debug JSON. Open those values on demand in a bounded panel or modal with byte and character size indicators. Truncate only the display summary; the full resolved values remain inspectable without being rendered into the main layout.

## 7. Progressive disclosure (U3)
By default show model, provider, class, size, steps/CFG, strategy, and lifecycle state. Behind `View what will be submitted`, show exact prompt, negative prompt, seed, all resolved settings, and raw payload metadata. Keep the full view collapsed by default.

## 8. Honest-state / no-black-box notes (U5/U6)
The badge must provide the mandatory `View what will be submitted` affordance for every generation or edit action. State labels use backend vocabulary, including `cold`, `warming`, `warm`, `draft`, `stale`, `submitted`, `complete`, and `failed`; no spinner or `unavailable` label may conceal a known state. A stale resolution blocks submission until the upstream values are reconciled.

## 9. Acceptance
- The compact badge exposes model, provider, class, size, steps/CFG, strategy, and state at a glance.
- A user can inspect the exact resolved submission, including prompt and seed, without a main-flow blob (U3/U4/U6).
- `stale`, `cold`, and `warming` remain visible using backend vocabulary (U5).
- Missing or failed resolution is explicit and never replaced by guessed settings.
