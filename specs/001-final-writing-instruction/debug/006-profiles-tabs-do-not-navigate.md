# Debug 006: Profiles Page Tabs Do Not Navigate

**Created:** 2026-08-07
**Session:** N/A

## Report

The tabs on the profiles page (`ThemeProfiles.razor`) do not navigate when clicked. The page supports a `tab` query parameter and reads it in `OnParametersSetAsync`, but tab button clicks only assign the private `_activeTab` field.

## Analysis

The tab buttons use handlers such as `@onclick='() => _activeTab = "tone"'`. This changes component state only. It does not update the browser URL or route/query state. The component already has `[SupplyParameterFromQuery(Name = "tab")] public string? Tab` and `_validTabs`, so the intended navigation state is query-backed but the click handlers bypass that path.

Authoritative artifacts consulted:

- `specs/001-final-writing-instruction/spec.md`
- `specs/001-final-writing-instruction/tasks.md`
- `specs/001-final-writing-instruction/research.md`
- `specs/001-final-writing-instruction/plan.md`
- `specs/001-final-writing-instruction/data-model.md`
- `specs/001-final-writing-instruction/contracts/slot-17-output-contract.md`
- `specs/001-final-writing-instruction/contracts/terminology-mapping.md`
- `specs/001-rp-prompt-redesign/spec.md`

## Plan

Update `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor` to use one tab-selection handler that sets `_activeTab` and navigates to the current profiles route with `?tab=...`, preserving `ProfileId`. Update all profile tab buttons to call that handler. Build the web project for Razor compilation validation.

## Resolution

Pending implementation.

## Validated

- [ ] Web build passes
- [ ] Clicking each profile tab updates the URL and displays the selected panel
