# 085 — Composition prompt leaked a profile key and named the camera holder

**Report**

- Session: `8bc36efb-b235-485b-8675-b98ad7754e59`
- Studio interaction: `5d5ce679-94c2-46d2-9eee-d2bf50985193`
- Production group: `684bf7fd-2cc0-4ee2-baee-65e6cee82eaf` (Composition, POV Becky)
- Compiled brief: `272d7280-6624-4146-9311-65e771af7370`
- Prompt records: `8b694b71` / `15e4425f` / `8655924e` (all `scene_beat_production`-independent, Krea 2 target,
  `qwen2.5-vl-7b-instruct-abliterated-local` compiler)
- Error: `Prompt generation failed: The generated image prompt contradicts the request, and one corrective pass did
  not fix it: The prompt contains the internal participant key "…towards p0…" … [Becky] The prompt names Becky, who is
  the camera and is not in the picture.`
- Timestamp: 2026-10-27 00:35–00:44 local.

**Analysis**

The compiler input itself contained both forbidden values, so the retry could not succeed.

1. The moment enrichment recorded `facing.toward` as the phrase `"toward p0"` instead of the bare profile key
   `p0`. `SceneImagePovProjection` prints that world fact verbatim in the in-frame block
   (`facing: "toward p0"`), and its exact-match comparison treated the phrase as naming nobody — so it also derived
   the *opposite* facing (`faces AWAY from the camera`) for someone recorded as facing the viewpoint.
2. The v4 world body (`at.posture` + `at.anchor`) is recorded in world terms: Dean's posture was
   `"standing over Becky on the linoleum"`. `ToProjected` anonymised the prose facts (position, action, sightline,
   clothing, physical location) but not `WorldBody`/`WorldFacing`, so the holder's name reached the caption input.
3. `StillBriefFactSheet.RenderReferences` printed `typedReferences[].subjectKey` directly, producing
   `REFERENCES: - p1 / - p1 / - p1` in the same prompt.

The validator was correct to refuse: `p0`/`p1` are not words an image model knows (and a parenthesised key is weight
syntax), and the POV holder is the camera, never a person in the picture.

**Plan**

Stop the internal identifiers and the camera holder's name at the SOURCE: canonicalise the recorded facing target,
translate every key in projection/fact-sheet text to a word, and anonymise the v4 world facts with the same rule the
prose facts already use. Keep the strict validator.

**Resolution**

- `SceneMomentEnrichmentParser.NormalizeFacingTarget` canonicalises `"toward p0"` / `facing Becky` to the profile key at
  parse time.
- `SceneImagePovProjection` now builds a profile-key → name map from the cast and passes it into `ToProjected`;
  `WorldBody` and `WorldFacing` are anonymised and every remaining key is rendered as the name, `"the camera"` for the
  viewpoint, or `"the other person"`. `Anonymize` is null-safe, and `NamesTarget` unwraps a leading
  toward/facing/to/at preposition before matching, so the derived facing is no longer inverted.
- `StillBriefFactSheet` resolves keys once for every rendered value (plain facts, cast, frame, event, references) and
  renders references by ROLE + person phrase (`the face of Dean`, `the place`), never by key. Both builders pass the
  projection's viewpoint key and in-frame names into the sheet.
- `NaturalLanguageSceneImagePromptBuilder` decides the anonymous `PRODUCTION POV:` line from the locally resolved
  projection rather than the optional parameter, and `PonySceneImagePromptBuilder` now emits the same anonymous line
  for a participant POV (it still printed `PRODUCTION POV: Becky`).

**Validated**

- [x] Rebuilt the real compiled brief through `Krea2SceneImagePromptBuilder`: the new user prompt contains zero
  `p0`/`p1` tokens and never names Becky; the in-frame block reads `facing: "toward the camera"` and
  `body: "standing over the camera on the linoleum at kitchen linoleum"`.
- [x] Affected test suites passed: 443 passed, 0 failed on 2026-10-27 (projection, fact sheet, enrichment parser,
  SDXL/Pony/registry builders, prompt plan and reference-role tests).
- [x] New regressions cover a preposition-wrapped facing target, the v4 world-body name leak, fact-sheet reference
  resolution, and the enrichment canonicalisation.
- [ ] Live re-run of this Composition after the app republish not yet done.
