# ReferencePicker

## 1. Job (U1)
Choose an approved versioned reference.

## 2. Reused by
- Studio: Identity.
- Studio: Composition.
- Bootstrap: Promote.
- Reference Library.
- Location: Geometry.

## 3. Input contract
All types below are proposed design types, not final APIs.

- `ReferenceKind Kind` - face view set, wardrobe, location, or control-map reference.
- `ReferenceQuery Query` - version, approval, angle, and search filters.
- `IReadOnlyList<ReferenceSummary> References` - current bounded page of choices.
- `int PageSize` - maximum summaries requested per page.
- `Guid? SelectedReferenceId` - current selection.
- `IReadOnlyList<ReferenceAngle> RequiredAngles` - angle-aware views to surface (FR-C3-05).
- `bool ApprovedOnly` - whether the consuming stage permits only approved references.

## 4. Output/events
- `EventCallback<ReferenceSelectedEventArgs> OnSelected` - selects a reference version.
- `EventCallback<ReferencePageRequestedEventArgs> OnPageRequested` - loads another bounded page.
- `EventCallback<ReferenceOpenedEventArgs> OnOpen` - opens the selected reference and views.
- `EventCallback<ReferenceApprovalViewRequestedEventArgs> OnApprovalDetails` - opens approval provenance.

## 5. States
- `empty`: no references meet the filters.
- `loading`: page or angle set is loading.
- `error`: reference or angle loading failed with retry.
- `populated`: references are available.
- Approval states: `draft`, `approved`, `stale`, `rejected`.
- `incomplete`: the selected view set lacks one or more required angles.
- `selected`: one version is selected and its approval state is visible.

## 6. Data-volume behaviour (U4)
Use server-side paging for the reference list and virtualize the visible rows or tiles; never render the full library. Load thumbnails first and load full-resolution views only when a reference is opened. Fetch angle images on demand for the selected set, with a bounded number of previews visible at once.

## 7. Progressive disclosure (U3)
By default show thumbnail, name, version, approval state, and required angle availability. Behind an expander show approval history, frozen text, provenance, source payload metadata, and all angle details. Full-resolution views open only after selection.

## 8. Honest-state / no-black-box notes (U5/U6)
The picker itself does not trigger generation, but a host edit/generation action must expose `View what will be submitted`. Approval labels must use `draft`, `approved`, `stale`, and `rejected` exactly. An incomplete or disallowed reference is visibly blocked; it cannot be silently replaced with another version.

## 9. Acceptance
- The list uses paging or virtualization and keeps the full library out of the DOM (U4).
- Thumbnails are shown first and full-resolution views open on demand.
- Approval state and version are visible on every choice, including `stale` and `draft` (U5).
- Required angle-aware views are surfaced and incomplete sets cannot appear complete (FR-C3-05).
- The selected reference remains stable across paging and navigation (U7).
