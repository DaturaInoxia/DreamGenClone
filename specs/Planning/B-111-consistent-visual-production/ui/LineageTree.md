# LineageTree

## 1. Job (U1)
Inspect and branch attempt history.

## 2. Reused by
- Studio: Lineage.
- Asset Manager.

## 3. Input contract
All types below are proposed design types, not final APIs.

- `Guid ProductionGroupId` - production group whose attempts form the tree.
- `IReadOnlyList<LineageNodeSummary> Nodes` - visible node slice for the current viewport.
- `LineageQuery Query` - stage, branch, status, and cursor filters.
- `Guid? SelectedNodeId` - currently focused attempt.
- `LineageExpansionState Expansion` - expanded branch identifiers and lazy-load state.
- `LineageNodeDisplayMode DisplayMode` - compact tree or review mode.

## 4. Output/events
- `EventCallback<LineageNodeSelectedEventArgs> OnSelected` - selects an attempt.
- `EventCallback<LineageChildrenRequestedEventArgs> OnChildrenRequested` - loads a bounded child slice.
- `EventCallback<LineageBranchRequestedEventArgs> OnBranch` - requests a branch from a completed attempt.
- `EventCallback<LineageAttemptOpenedEventArgs> OnOpenAttempt` - opens attempt detail.
- `EventCallback<LineagePageRequestedEventArgs> OnPageRequested` - requests another bounded node page.

## 5. States
- `empty`: no attempts exist for the production group.
- `loading`: tree roots, children, or thumbnails are loading.
- `error`: a branch or node slice failed with retry.
- `populated`: nodes and branch relationships are visible.
- `collapsed`: children are not rendered until expanded.
- `expanded`: selected branch children are loaded into the virtualized viewport.
- `selected`: one attempt is focused.
- `branching`: branch request is being confirmed and submitted.
- `stale` or `complete`: node lifecycle state is visible.

## 6. Data-volume behaviour (U4)
Virtualize the tree rows and lazy-load children when a branch expands; page or cursor the backing node query. Never render the complete lineage. Show thumbnails first for node previews and load full resolution only when an attempt is opened. Keep logs, prompts, and score histories on demand in the attempt detail panel.

## 7. Progressive disclosure (U3)
By default show stage, branch, attempt state, timestamp, thumbnail, and the selected node's short score summary. Behind an expander show full scorecard, prompt/payload metadata, run details, and provenance. Children stay collapsed until the user expands a branch.

## 8. Honest-state / no-black-box notes (U5/U6)
The tree itself does not trigger generation, but Branch or any host edit action must provide `View what will be submitted` before execution. Display backend states such as `draft`, `stale`, `complete`, `failed`, and `aborted` on nodes. Branching is unavailable for non-completed attempts and cannot silently select a different parent.

## 9. Acceptance
- The tree virtualizes rows, lazy-loads children, and pages or cursors its backing query (U4).
- Thumbnails are shown first and full-resolution attempt media opens explicitly.
- A branch can be requested from any completed attempt and the chosen parent remains visible.
- Node lifecycle states and branch relationships use explicit backend vocabulary (U5).
- Prompt, payload, logs, and score details remain behind collapsed, on-demand views (U3/U4).
