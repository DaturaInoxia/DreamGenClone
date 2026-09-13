# Faces section — UI notes plan (user notes 001–005)

**Source:** user notes supplied 2026-09-12, transcribed in order. This document supersedes the
**layout** parts of `ui-contract.md` (§1, §2, §7, §9) where they conflict; the per-step *content*
requirements of the contract still stand unless a note below says otherwise.
**Scope:** the **Faces** section of the Character Studio (`/characters/{characterId}`).
**Status:** approved to implement ("do all the notes now"). Open forks listed in §5 — implementation
proceeds on the recommended default unless overruled.

---

## 1. Requirements from the notes

### R1 — Validate is automatic and lives on the image (note 001)
- Validate runs **automatically when the front image is created** — it is no longer a manual
  "Run validation" action.
- The **validation results are stored in the image's metadata** (measurement + verdict travel with the
  image), not only on the build step row.
- **A failed validation sets that image's status to Rejected** automatically.

### R2 — Steps 3/4/5 happen in the edit form, along the lineage (note 002)
- De-clothe (3), Crop (4) and Enhance (5) are all performed in the **Edit Image form** (the shared
  `ImageEditWorkspace`), moving through the **lineage**; that form must allow all three.
- The **image metadata records done or skipped** for each of the three (skipped is a recorded state).
- **Edited images appear in the image list the same as generated ones**, and in the **review deck**.

### R3 — Steps 1–5 produce the canonical front; angles chain per side (note 003)
- Steps 1–5 exist to build the **canonical Front Profile**.
- The other angles **derive from it**, and **each angle derives from the previous angle**.
- The chaining is done **for left and right separately**: `front → 3/4L → ProfileL` and
  `front → 3/4R → ProfileR`.

### R4 — Pack creation is gated on a minimum of 5 face angles (note 004)
- Once the minimum of 5 face angles exists, the **Identity Pack can be created** (step 7 / Promote).

### R5 — Two panel groups replace the stacked steps (note 005)
**Panel A — "1 Front · 2 Validate" (combined heading)**
- prompt editor, model selection, **Generate image**, upload
- the **generated candidates**
- a candidate must be chosen with **"Use this"**; that selection is the input to Panel B

**Panel B — "3 De-clothe · 4 Crop · 5 Enhance" (combined heading)**
- shows the candidate chosen in Panel A as its input
- hosts the existing **"Garment removal · unified image edit"** block (moved here)
- the user de-clothes, crops, enhances — **this is a list**, several attempts may be taken per step
- from this list the user **picks a picture and approves it** → that becomes the
  **Canonical Front Profile Image**
- everything after (angles, pack) starts from that image

---

## 2. Where this lands relative to today's code

| note | today | change |
|---|---|---|
| R1 | Validate is a manual button; result on the build step row (`CharacterIdentityValidationResult`); no per-image verdict; nothing auto-rejects | trigger validation when a front attempt completes; persist result on the image; reject the image on failure |
| R2 | Garment step embeds `ImageEditWorkspace`; the form already has Edit/Identity/**Crop**/**Enhance** tabs (debug 053/054 work) | add per-image done/skipped markers; include edited images in the image list + review deck |
| R3 | no angle chain exists (steps 4–7 have no service) | new: per-side chain driving edits from the canonical front |
| R4 | no gate | gate Promote on the angle minimum |
| R5 | one card: `<ol>` of 7 steps + stacked panels for 3 of them | two panel groups; rail reduced to the group headers + Angles/Promote |

**Known blockers / wrinkles**
- `CharacterIdentityFrontService.SelectFrontAsync` requires the image to belong to the build's **front
  container**, and it completes the **Front** step — so Panel A's "Use this" maps onto it directly, and
  Panel B's "Approve" can reuse it **only if** the chosen edit result is an image of that same container
  asset. Verify before wiring.
- The **pack's** canonical face (`CanonicalFaceAssetId`) is currently set at **pack approval**
  (`ApprovePackAsync(packId, descriptorSnapshotJson, canonicalFaceAssetId)`) and must be an approved
  `Face` asset of that pack. At R5's Approve point **no pack exists yet** — see fork F1.
- The edit form's lineage labels every non-edit row "Source" (crop/enhance rows are mislabelled). With
  lineage as Panel B's list this must be fixed first (tracked in debug record 053).

---

## 3. Work order (each slice builds + tests green before the next)

1. **Slice 1 — Panel groups (R5 layout).** Faces section becomes Panel A (Front+Validate) and Panel B
   (De-clothe/Crop/Enhance) with the existing behaviour rewired into them; explicit "Use this" gate in A;
   Panel B shows A's selection and hosts the edit block. Angles/Promote keep their status rows.
2. **Slice 2 — Per-image validation result (R1).** Front attempt completion triggers validation; result
   persisted in the image metadata; failure sets the image Rejected; Panel A shows verdict per candidate.
3. **Slice 3 — Done/skipped markers + visibility (R2).** Edit images record which of 3/4/5 they represent
   (done or skipped); edited images appear in the image list and review deck alongside generated ones.
4. **Slice 4 — Canonical front (R5 approve + F1).** Approve on a Panel B attempt sets the canonical front
   the angles chain from.
5. **Slice 5 — Angles chains (R3).** Left and right chains, each link an edit of its predecessor.
6. **Slice 6 — Promote gate (R4).** Pack creation enabled only once the angle minimum is met, naming
   what's missing.

---

## 4. Files expected to change

- `DreamGenClone.Web/Components/Pages/CharacterStudio.razor` (+ `.razor.css` if present) — slices 1, 2, 4, 6
- `DreamGenClone.Web/Application/RolePlay/CharacterIdentityFrontService.cs`, `…ValidationService.cs`,
  `…GarmentService.cs` — slices 2, 3, 4
- `DreamGenClone.Domain/RolePlay/CharacterIdentityBuildModels.cs` — canonical-front + step-marker storage
- `DreamGenClone.Infrastructure/RolePlay/*Repository.cs` (+ schema) — persistence for the above
- `DreamGenClone.Web/Application/RolePlay/Editing/*` — slice 3 (edited-image visibility, lineage labels)
- review deck (`Components/Pages/ReviewDeck.razor`, `SceneAssetService`) — slice 3
- tests: `DreamGenClone.Tests/RolePlay/CharacterIdentity*Tests.cs`, `SceneAsset*Tests.cs`

---

## 5. Forks — recommended defaults (proceeding unless overruled)

**F1 — Where the canonical front lives before a pack exists.**
*Recommended:* a **build-level `CanonicalFrontAssetId`** on `CharacterIdentityBuild`. Panel B's Approve
sets it; the Promote step (R4) then promotes the canonical front as the pack's face asset and, at pack
approval, becomes `CanonicalFaceAssetId`. Avoids inventing a pack early and keeps one canonical notion
per lifecycle stage.

**F2 — Per-candidate validation vs one verdict per build.**
*Recommended:* **per image** (as R1 says) — each front attempt carries its own result and Rejected state;
the build's Validate step status is derived from the selected candidate, not stored separately.

**F3 — What "5 face angles" counts.**
*Recommended:* the canonical front **plus** four angles = the five pack views (front + 3/4L + 3/4R +
ProfileL + ProfileR). Extra angles may exist but the gate is five.
