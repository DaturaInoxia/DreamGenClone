# 002 — Continuation Settings Popup Renders Empty Body

**Created:** 2026-08-13
**Feature:** B-082 continuation settings popup.
**Follows:** `debug/001-popup-top-clipped.md`

## Report

After fixing the clipping (001), the popup rendered its header ("Continuation Settings") and footer buttons ("Clear all / Cancel / Done") but **none of the setting rows** were visible. User reported: "no it is not clipped but it does not show anything at all."

## Analysis

The popup root is a flex column:

```css
.rw-continuation-settings-overlay-popup {
    ...
    max-height: calc(100vh - 40px);   /* no definite height */
    display: flex;
    flex-direction: column;
    overflow: hidden;
}
.rw-continuation-settings-body {
    flex: 1 1 0;       /* flex-basis: 0 */
    min-height: 0;
    overflow-y: auto;
}
```

In a column flex container that has only `max-height` (an **indefinite** height), the container's height is determined by its content. The body has `flex-basis: 0`, so it contributes 0 height; `flex-grow: 1` has no remaining space to fill because the container is exactly as tall as its (header + 0-height body + footer) content. The body therefore collapses to **0px**, and `overflow-y: auto` + the parent's `overflow: hidden` hide all rows.

This is why the header and footer rendered but the rows did not — the rows were present in the DOM but zero-height.

## Plan

Give the overlay popup a **definite height** so `flex: 1 1 0` on the body has concrete remaining space to fill, then the body scrolls:

```css
.rw-continuation-settings-overlay-popup {
    height: min(85vh, 720px);
    max-height: calc(100vh - 40px);
    ...
}
```

## Resolution

- `roleplay-workspace.css`: added `height: min(85vh, 720px);` to `.rw-continuation-settings-overlay-popup`.

CSS-only change — the app serves `wwwroot` directly; a hard refresh (Ctrl+F5) is required to bypass the browser stylesheet cache.

## Validated

- [ ] Pending
