# CandidateGrid

## 1. Job (U1)
Curate candidates through comparison decisions.

## 2. Reused by
- Bootstrap: Curate.
- Bootstrap: Expand.
- Studio candidate review.
- Validation Review (P6).

## 3. Input contract
All types below are proposed design types, not final APIs.

- `Guid CandidateSetId` - set being curated.
- `IReadOnlyList<CandidateSummary> Candidates` - page or viewport summaries, not an unbounded render list.
- `CandidateGridQuery Query` - page, sort, filter, and comparison options.
- `int PageSize` - maximum candidates requested per page.
- `Uri ThumbnailBaseUri` - thumbnail source for each candidate.
- `CandidateDecisionPolicy DecisionPolicy` - allowed accept/reject/pending decisions.
- `bool AllowSideBySide` - whether selected candidates can be compared.

## 4. Output/events
- `EventCallback<CandidateDecisionChangedEventArgs> OnDecisionChanged` - accepts, rejects, or resets a candidate decision.
- `EventCallback<CandidateComparisonRequestedEventArgs> OnCompare` - opens selected candidates side by side.
- `EventCallback<CandidateOpenedEventArgs> OnOpen` - opens one candidate at full resolution.
- `EventCallback<CandidatePageRequestedEventArgs> OnPageRequested` - requests another bounded page.
- `EventCallback<CandidateSelectionChangedEventArgs> OnSelectionChanged` - changes comparison selection.

## 5. States
- `empty`: no candidates match or the set has not run; show the next available action.
- `loading`: page or thumbnails are loading.
- `error`: page or image load failed with retry for the affected request.
- `populated`: candidates are available with current decisions.
- `comparing`: two or more selected candidates are shown side by side.
- `saving`: a decision is being persisted without blocking other page interaction.
- `partial`: some thumbnails are available while remaining thumbnails load.

## 6. Data-volume behaviour (U4)
Use virtualization for the candidate tile viewport and server-side paging for the full set; never render the complete gallery. Request only the visible tile range and dispose thumbnails outside the viewport. Show thumbnails first, with full resolution loaded only on open or in the bounded side-by-side comparison. Persist decisions per candidate so paging or refresh cannot erase them.

## 7. Progressive disclosure (U3)
By default show thumbnail, candidate identifier, approval decision, and the small set of comparison facts needed to act. Behind an expander show scorecard details, prompt/provenance, raw payload metadata, and rejection rationale. Full-resolution media and all candidate metadata open on demand.

## 8. Honest-state / no-black-box notes (U5/U6)
This component does not itself trigger generation, but any parent generation action must provide `View what will be submitted`. Display backend decision vocabulary such as `draft`, `approved`, `rejected`, and `stale` rather than replacing it with color-only labels. Saving a decision must show its persistence state.

## 9. Acceptance
- The grid virtualizes visible tiles and pages the backing candidate set; no unbounded gallery is rendered (U4).
- Thumbnails load before full-resolution images, and full resolution opens explicitly (U4).
- Accept/reject decisions persist per candidate and remain intact across paging.
- Side-by-side comparison is bounded and can be entered from the same candidate selection.
- State labels and decision persistence are visible and use backend vocabulary (U5).
