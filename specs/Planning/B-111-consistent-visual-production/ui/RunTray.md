# RunTray

## 1. Job (U1)
Monitor runs and control jobs.

## 2. Reused by
- Runs page.
- Compact embedded form in Studio: Composition.
- Compact embedded form in Bootstrap: Curate.
- Compact embedded form in Asset Manager.
- Embedded in Strategy Qualification.

## 3. Input contract
All types below are proposed design types, not final APIs.

- `Guid? RunId` - selected run, if the tray is scoped to one run.
- `RunSummary Summary` - counts and aggregate status shown first.
- `IReadOnlyList<JobSummary> Jobs` - visible virtualized job rows.
- `RunQuery Query` - status filter, sort, and viewport cursor.
- `TimeSpan RefreshInterval` - live status refresh cadence supplied by the host.
- `bool CanAbort` - whether the current actor may request an abort.
- `RunPresentationMode PresentationMode` - global or compact embedded layout.

## 4. Output/events
- `EventCallback<JobSelectedEventArgs> OnJobSelected` - opens one job's details.
- `EventCallback<RunSelectedEventArgs> OnRunSelected` - changes the active run.
- `EventCallback<RunAbortRequestedEventArgs> OnAbort` - requests a confirmed abort.
- `EventCallback<RunPageRequestedEventArgs> OnPageRequested` - requests another bounded job page.
- `EventCallback<RunRefreshRequestedEventArgs> OnRefresh` - requests live status refresh.

## 5. States
- `empty`: no runs or jobs match the current scope.
- `loading`: summary or job status is being fetched.
- `error`: status retrieval failed, with last known state and retry.
- `populated`: summary and jobs are available.
- Run lifecycle states: `staged`, `warming`, `active`, `draining`, `complete`, `failed`, `aborted`.
- Provider states: `cold`, `warming`, `warm`.
- `aborting`: confirmation accepted and abort is in progress.

## 6. Data-volume behaviour (U4)
Summary is rendered first. The job list is virtualized and backed by cursor or server-side paging; it never renders an unbounded queue. Load per-job detail, logs, and submitted payload only after selecting a job. Show counts and compact status rows before loading any large media or diagnostic blob.

## 7. Progressive disclosure (U3)
By default show run counts, provider state, lifecycle state, progress, and the primary control. Behind an expander show individual jobs, timing, retry information, scores, payload metadata, and logs. Full prompts and raw JSON open in an on-demand panel with a size indicator.

## 8. Honest-state / no-black-box notes (U5/U6)
Use the backend vocabulary exactly: `cold`, `warming`, `warm`, `staged`, `draining`, and `complete` are visible states, not hidden behind a generic spinner. For an embedded tray, any generation/edit form still requires a `View what will be submitted` affordance before `Run`. Never label a cold provider as active or silently change its strategy.

## 9. Acceptance
- The tray is summary-first, with counts visible before per-job detail (U4).
- The job list is virtualized and live status updates do not render an unbounded queue.
- `cold`/`warming`/`warm` and `staged`/`draining`/`complete` are shown with backend vocabulary (U5).
- Abort requires confirmation and communicates that queued or in-flight work will be stopped, including known cost consequences (U9).
- A draining run never freezes the page; users can inspect data and queue independent work (U8).
