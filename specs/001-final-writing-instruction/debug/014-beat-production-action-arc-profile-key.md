# 014 — Beat Production action-arc profile key and source authority

**Report**

- Session: `4f2eec18-b190-4beb-ad35-8d520ae5c800`
- Catalogue: `f0a98ca5-c92e-4887-b607-1b63f76597fd`
- Beat: `b3`
- Initial error: `scene_beat_production_output_invalid Unknown Beat Production profile key 'door'.`
- Follow-up error: `scene_beat_production_output_invalid Beat Production exact source text does not match evidence 'n0' at [248, 361).`
- Symptom: GLM returned object identifiers in participant fields and later returned generated prose in `exactSourceText` instead of the referenced evidence span.

**Analysis**

The strict parser resolves `actionArc.subjectKey` and `targetKey` through the selected participant profile map. The contract prompt already stated that object identifiers belong in `targetObject`, but `SceneBeatProductionContract.CreateResponseSchema()` represented both action-arc references as unrestricted strings. The provider therefore had no schema-level vocabulary constraint and could emit `door` or `door-4`.

The durable handler already deserializes the immutable `SceneBeatProductionSourceSnapshot` before calling the structured completion client. The selected snapshot contains the authoritative participant profile keys, so it is the single correct source for the schema enum. The parser remains the final strict validator; no fallback conversion is appropriate.

The later source-text failure exposed a second contract issue. `exactSourceText` is a model-visible duplicate of immutable evidence, so treating it as authoritative allowed harmless model paraphrasing to invalidate an otherwise valid span. The resolver now validates only the evidence key and offset bounds, then derives the canonical text from the persisted snapshot. The parser uses that resolved text for both persistence and spoken-text normalization. The model field remains accepted for schema compatibility but cannot alter the authoritative source text.

Specification artifacts consulted:

- `specs/001-final-writing-instruction/spec.md`
- `specs/001-final-writing-instruction/plan.md`
- `specs/001-final-writing-instruction/contracts/slot-17-output-contract.md`
- `specs/001-rp-prompt-redesign/spec.md`

**Plan**

Constrain `actionArc.subjectKey` and nullable `targetKey` to the participant profile keys from the immutable source snapshot. Update the contract test to verify the generated enum and update the durable handler to pass the snapshot keys. Keep object references in `targetObject` and preserve strict parser rejection for invalid output.

**Resolution**

- `SceneBeatProductionContract.BuildMessages` now creates the response schema from `snapshot.Profiles`.
- `SceneBeatProductionContract.CreateResponseSchema` now requires a non-empty profile-key set.
- `actionArc.subjectKey` uses a profile-key enum.
- `actionArc.targetKey` uses the same profile-key enum plus `null`.
- `SceneBeatProductionPlanJobHandler` passes the persisted snapshot profile keys to the schema.
- Contract tests verify both enum vocabularies.
- `SceneBeatProductionSourceResolver.ResolveExactSpan` derives `ExactText` from the immutable evidence snapshot after validating the evidence key and offsets.
- `SceneBeatProductionParser.ParseDialogue` validates normalization against `span.ExactText` and persists `span.ExactText`, leaving no second model-controlled source-text decision path.

**Validated**

- [x] Focused contract and parser tests passed: 25 passed, 0 failed on 2026-09-04.
- [x] Web application build passed after the source-authority changes.
- [x] Fresh durable Beat Production run completed: catalogue `f0a98ca5-c92e-4887-b607-1b63f76597fd`, beat `b3`, plan version `14`, attempt `8fb69582-1222-48a7-a20f-35b332180811`, provider `OpenRouter / z-ai/glm-4.7`.
- [x] Runtime validation completed with `ValidationDetailsJson = {}` and durable job status `Complete`; generated plan contained 3 sound events, 1 video coverage item, 0 dialogue cues, and 0 review items.
