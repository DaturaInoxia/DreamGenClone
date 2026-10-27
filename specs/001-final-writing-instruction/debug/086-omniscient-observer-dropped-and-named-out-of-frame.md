# 086 — Omniscient frame dropped a present observer and then named them out of frame

**Report**

- Session: `8cdc3a8f-4966-469e-9523-33185ed74466`
- Studio interaction: `569c3531-6d08-40c6-9fa0-bfd76053df1e`
- Production group: `58b521c4-8181-4096-9d8d-aac0f33772bf` (Composition, POV **Omniscient**, Krea 2)
- Prompt records: `7c747241` / `5dc4a03d` / `1e4a0666` (compiler `qwen2.5-vl-7b-instruct-abliterated-local`)
- Error: `Prompt generation failed: The generated image prompt contradicts the request, and one corrective pass did
  not fix it: [Ken] The prompt names Ken, who is not in this POV's frame. Do not depict or name them. The caption
  describes what is outside the picture ("out of frame").`
- Timestamp: 2026-10-27 10:31–10:33 local.

**Analysis**

The projection classified Ken as a remote observer because he is `involvement: observer` and his recorded place
("by the kitchen counter") is not the exact same string as the scene location ("Husband and Wife Trailer — Shared
Private Space - the living room couch"). He was therefore removed from `InFrame`, even though:

- he stands a few feet away in the same open trailer interior, and
- the compiled blocking seated him at RIGHT, background — the fact sheet's FRAME block still listed him there.

The prompt then carried both contradictions: the projection said "the only person in the picture" while the frame
listed two people, and the projection printed a negative list — `Not in the active scene …: Ken` — which the caption
writer paraphrased into "Ken … out of frame". That tripped both blocking checks: naming a person the projection
excluded, and describing what is outside the picture.

The exact-string location comparison is the same defect already documented for participant viewpoints (`027`,
`Resolve_ParticipantInTheScene_IsNotDescribedAsARemoteObserver`): an "observer" is a narrative role (no meaningful
part in the beat), not a spatial fact.

**Plan**

Decide remote-observer status from an explicit SEPARATION fact instead of a location-string mismatch, and never write
an excluded person's name into the prompt, not even as a negative list.

**Resolution**

- `SceneImagePovProjection.IsRemoteObserver` now requires the record to state a separation — outside/outdoors, a yard,
  another room, through the window or glass, upstairs/downstairs, another vehicle — across `physicalLocation`,
  `position`, `actionOrObservation` and `sightline`. An observer whose record states no separation is present in the
  active space and stays in an Omniscient frame; a genuinely remote one (e.g. "yard outside the window") still drops
  out and no longer holds a seat.
- The projection no longer emits `Not in the active scene …: {names}`. The in-frame list plus "Do not add any person
  who is not listed above" already define the cast; naming an excluded person only feeds the caption writer that name.
- The existing out-of-frame regression now also asserts the excluded name never appears in the prompt text.

**Validated**

- [x] Rebuilt the real compiled brief through `Krea2SceneImagePromptBuilder`: both Becky (LEFT, foreground) and Ken
  (RIGHT, background) are in frame, `OutOfFrame` is empty, and the prompt contains no "out of frame" or
  "Not in the active scene" wording.
- [x] Affected test suites passed: 444 passed, 0 failed on 2026-10-27.
- [x] New regression `Resolve_Omniscient_KeepsAnObserverWhoStandsInTheSameRoom`; the yard-observer test still drops
  the observer and now checks the prompt does not name them.
- [ ] Live re-run of this Composition after the app republish not yet done.
