# FrozenTextBlockEditor

## 1. Job (U1)
Edit validated invariant reference text.

## 2. Reused by
- Bootstrap: Describe, as a draft editor.
- Bootstrap: Promote.
- Reference Library.

## 3. Input contract
All types below are proposed design types, not final APIs.

- `Guid? ReferenceId` - reference whose invariant text is being edited.
- `FrozenTextBlock TextBlock` - current text, version, and approval metadata.
- `int MaxCharacters` - configured maximum text length.
- `bool IsDraft` - whether the editor is creating a draft.
- `TextValidationPolicy ValidationPolicy` - non-empty and other configured validation rules.
- `EditConcurrencyToken VersionToken` - version used to prevent stale overwrite.

## 4. Output/events
- `EventCallback<FrozenTextChangedEventArgs> OnChanged` - reports draft text changes.
- `EventCallback<FrozenTextSaveRequestedEventArgs> OnSave` - persists a validated draft.
- `EventCallback<FrozenTextValidationRequestedEventArgs> OnValidate` - requests validation.
- `EventCallback<FrozenTextCancelRequestedEventArgs> OnCancel` - leaves without discarding persisted state.

## 5. States
- `empty`: no text exists; approval is unavailable until non-empty text is supplied.
- `loading`: text or version metadata is loading.
- `error`: load, validation, or save failed with preserved draft.
- `populated`: text is present and editable.
- `dirty`: local edits differ from the persisted version.
- `invalid`: empty or otherwise outside the configured validation policy.
- `saved`: current draft is persisted.
- `stale`: the persisted reference changed since this editor loaded.

## 6. Data-volume behaviour (U4)
Enforce the configured character limit and show a live character count. Keep the editor body bounded with scrolling rather than expanding the page. Show version history and prior text in a paged or virtualized list, and open prior full text on demand. Do not inline raw provenance blobs.

## 7. Progressive disclosure (U3)
By default show the current text, validation state, character count, save action, and approval/version summary. Behind an expander show validation rules, prior versions, provenance, and change history. Keep the history collapsed and bounded.

## 8. Honest-state / no-black-box notes (U5/U6)
This editor does not trigger generation. If a host subsequently generates or edits from the text, that host must provide `View what will be submitted`. Show `draft`, `saved`, `stale`, and `invalid` using explicit labels; never imply approval merely because text is non-empty.

## 9. Acceptance
- Empty text cannot be approved or saved as an approvable invariant block (FR-C1-02).
- Dirty, saved, stale, and invalid states are visible and preserve the user draft during errors.
- Version history is bounded and full prior text opens on demand (U3/U4).
- Back or cancel does not silently discard persisted work, and a stale version requires explicit reconciliation (U7).
