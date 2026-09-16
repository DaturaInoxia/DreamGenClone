# 059 — Character Identity angle prompt and orphan attempt cleanup

## Report

- **Reported:** 2026-09-14, during Panel C angle curation.
- **Symptoms:** 3/4 Right displayed the left-facing prompt; left/right state appeared coupled; attempts 2–4 existed without distinct output images; selecting curation buttons could reselect the angle card.

## Analysis

- Both left and right views resolved the same `identity.angle.three-quarter` template, whose seed text explicitly says image-left.
- `ResultChanged` is raised when a shared workspace result becomes complete, but the host recorded every callback as a new attempt without deduplicating output artifact IDs.
- The angle cards contain buttons inside a clickable card, so curation clicks could bubble to angle selection and reset the workspace.
- Audit of build `49c3175356874e908b181cad815f7141` found duplicate attempt rows pointing to the same completed image `30753136591c4ca283cd163fcd00a39d`; no distinct outputs existed for the duplicate rows.

## Resolution

- Added distinct persisted prompt keys for right-facing 3/4 and profile views.
- Added output-artifact deduplication in result recording.
- Marked completed workspace results as complete attempts rather than pending attempts.
- Added click propagation suppression to `Use this` buttons.
- Removed ten duplicate/orphan attempt rows from the affected build's development database after auditing them.

## Validated

- [x] Invalid attempt rows audited and removed: 10 rows.
- [x] Touched-file diagnostics clean.
- [x] Web build succeeds.
- [ ] Runtime confirmation pending: select 3/4 Right, verify RIGHT prompt, run one edit, and confirm exactly one new attempt appears after completion.
