# 001 — Continuation Settings Popup Top Clipped

**Created:** 2026-08-13
**Feature:** B-082 continuation settings popup.

## Report

The continuation settings popup's top was clipped — it rendered anchored inside the prompt input area and could not overlay the Continue As buttons / prompt text input. User reported: "the top of the UI popup is being clipped, make it so it can overlay the button and prompt text input area."

## Analysis

The popup root used the shared `.rw-popup` class, which is positioned:

```css
.rw-popup {
    position: absolute;
    bottom: calc(100% + 4px);
    ...
}
```

This makes it pop **upward** from its nearest positioned ancestor — the `.rw-prompt-area` (`position: relative`) that also contains the Continue As row and the prompt text input. Any ancestor of `.rw-prompt-area` with `overflow: hidden` / `overflow-y: auto` (the response/chat scroll area) clips anything that extends above the prompt area, so the top of the popup was cut off.

The **Steer popup already solved this** by rendering inside a fixed full-viewport overlay (`rw-modal-overlay` + `.rw-steer-overlay-popup`, which overrides `position: absolute; bottom: calc(100% + 4px)` to `position: relative; bottom: auto`). The continuation settings popup had not been given the same treatment.

## Plan

Render the popup inside a `rw-modal-overlay` fixed overlay (identical pattern to the Steer popup), and neutralize `.rw-popup`'s absolute/bottom positioning via a dedicated overlay class so it centers in the viewport instead of anchoring to the prompt area:

1. `RolePlayWorkspace.razor` — wrap `<ContinuationSettingsPopup …/>` in `<div class="rw-modal-overlay" @onclick="CancelContinuationSettingsPopup">`.
2. `ContinuationSettingsPopup.razor` — add `rw-continuation-settings-overlay-popup` to the root and `@onclick:stopPropagation` (so clicks inside don't close it).
3. `roleplay-workspace.css` — add `.rw-continuation-settings-overlay-popup` (`position: relative; bottom: auto; …`) and `.rw-continuation-settings-body` (scrollable).

## Resolution

- `RolePlayWorkspace.razor`: popup wrapped in `rw-modal-overlay` with backdrop-click close.
- `ContinuationSettingsPopup.razor`: root gained `rw-continuation-settings-overlay-popup` + `@onclick:stopPropagation`; body changed from inline `max-height: 60vh` style to `.rw-continuation-settings-body` class.
- `roleplay-workspace.css`: added `.rw-continuation-settings-overlay-popup`, `> .rw-popup-header/.rw-popup-actions { flex-shrink: 0 }`, and `.rw-continuation-settings-body { flex: 1 1 0; min-height: 0; overflow-y: auto; padding: 4px 8px; }`.

Web project builds 0 errors.

## Validated

- [ ] Pending — user reported the popup then showed **no content** (see `debug/002-popup-empty-body.md`).
