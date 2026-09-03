# Phase 2 Asset Manager And Production Studio UI Contract

**Task:** P2-051  
**Frozen:** 2026-09-02  
**Baseline:** New sessions stamped with `SceneImageProductionSchema.CurrentGeneration` only.

## Existing Surface Decision

- `/asset-studio` remains the shared catalog route and becomes **Asset Manager**. Prompt/upload
  ingest may remain, but production browsing, filtering, provenance, typed version membership,
  approval, supersession, retention, and picker behavior use the shared `SceneAsset` catalog.
- `/roleplay/studio/{sessionId}/{interactionId}` keeps historical pipeline inspection for old
  sessions. New-session production navigation opens the production workspace described below and
  never exposes the legacy one-off render cards as a fallback.
- The current `SceneImageRecord` Production Studio block is transitional evidence. New production
  UI reads `ProductionIntentSnapshot`, `CompiledMediaRequest`, `ProductionWorkloadItem`,
  `ProductionAttempt`, `ProductionReviewDecision`, and `ProductionDerivative` only.
- Direct provider-request editing is hidden behind an explicit diagnostic/manual mode. Normal
  editing changes typed semantic intent and recompiles a new immutable request.

## Shared Shell

Both surfaces use a quiet workbench shell rather than nested cards:

1. A compact header contains route-level navigation, title, current scope, and primary command.
2. A tab row switches stable views; tab selection never clears entity selection.
3. A three-region workbench uses `minmax(0, ...)` tracks so dynamic labels cannot resize it:
   context/filter rail, primary canvas or result grid, and inspector.
4. The queue/attempt strip is a full-width bottom region with fixed thumbnail dimensions and
   horizontal overflow. Status badges never change thumbnail size.
5. Alerts occupy one stable status region below the header and receive focus only for blocking
   failures.

Desktop (`>= 1200px`) uses `280px minmax(480px, 1fr) 340px`. Tablet stacks the inspector below
the primary region. Mobile uses one column with context, primary region, inspector, then queue;
no horizontal page overflow is permitted. The media canvas uses a stable `16 / 10` aspect ratio,
`min-height: 240px`, and `object-fit: contain`.

## Asset Manager State

Stable state keys:

- `AssetTypeFilter`, `ApprovalFilter`, `CharacterFilter`, `SearchText`;
- `SelectedAssetId`, `SelectedIdentityVersionId`, `SelectedBodyVersionId`,
  `SelectedWardrobeVersionId`;
- `PickerMode`, `PickerRole`, `PickerActorKey`, `PickerReturnContext`;
- active tab: `Browse`, `Versions`, or `Lineage`.

Refresh preserves every key whose referenced record still exists. If a selected record was
deleted, selection moves to no record and the inspector states that explicitly; it never selects a
different version automatically. Picker confirmation returns exact asset/version/checksum and
semantic-role values. Filters apply to one shared catalog and are not reimplemented per picker.

## Production Studio State

Selection forms an ordered context path:

`Session -> Beat Production Plan version -> Moment Set version -> Moment -> Enrichment revision -> POV -> Intent -> Compiled Request -> Workload Item -> Attempt`.

Stable state keys:

- route `SessionId` and navigation `InteractionId`;
- `BeatProductionPlanId/Version`, `MomentSetId/Version`, `MomentId`,
  `MomentEnrichmentId/Revision`, and `Pov`;
- `SelectedIntentId`, `SelectedCompiledRequestId`, `SelectedWorkloadId`,
  `SelectedWorkloadItemId`, `SelectedAttemptId`;
- selected reference IDs/roles, media-pool filters, comparison attempt IDs, inspector tab, and
  diagnostic/manual-mode flag.

Changing an ancestor clears only invalid descendants. Polling refreshes status and output data but
never changes Moment, request, attempt, comparison, filter, or inspector selection. Returning to a
Moment restores its persisted draft workload and exact references; no values are inferred from
legacy interaction prose or current mutable configuration.

## Commands And Focus

- Tabs use buttons with `role="tab"`, `aria-selected`, and Left/Right arrow navigation.
- Rails and attempt strips use roving `tabindex`; Arrow keys move selection, Enter opens, and Space
  toggles comparison or picker selection.
- Icon-only commands use existing Bootstrap Icons and a `title` plus accessible label. Text remains
  for destructive or state-changing commands whose meaning is not universal.
- After selection, focus stays on the initiating control. After a successful mutation, focus moves
  to the resulting record only when the command creates one. Blocking errors receive focus through
  the shared status region; polling updates never steal focus.
- `Escape` closes preview/manual-request overlays and returns focus to their opener.

## Required Query And Action Surface

P2-052 adds typed shared-catalog queries and production approval/supersession/retention actions.
P2-053 adds session/Moment workload queries because `IProductionMediaRepository` currently loads
workloads only by ID. P2-054 adds semantic draft save/recompile plus review/approval orchestration;
repository writes remain behind application services. Razor components do not issue SQL, mutate
payload JSON, call provider adapters, or synthesize missing versions.

## Acceptance Checks

- Desktop and mobile screenshots show no overlap, clipped commands, or page-level horizontal
  scrolling.
- Switching Moment/request/attempt and polling preserve the state keys above.
- Keyboard-only browse, picker, comparison, prepare, submit, cancel, retry, review, reject, and
  approve paths retain visible focus.
- Exact intent, bindings, compiled request, provider ID, seed, checksums, review history, and
  derivative lineage are inspectable but secrets are absent.
- An old session shows create-new-session guidance before any production mutation and has no
  production action fallback.