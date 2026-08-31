# B-100 Provider Evidence and Canonical Input Matrix

**Purpose:** Ground the Beat/Moment production ontology and compiler contracts in documented inputs used by representative image, speech, sound, music, video, and lip-sync systems. This is design evidence, not a provider-selection decision.

## Design Rule

A field belongs in canonical B-100 data only when it expresses source-grounded production meaning that can be shared across modalities or must remain consistent between them. Provider names, model IDs, prompt syntax, sampling controls, output codecs, seeds, negative-prompt syntax, and provider capability workarounds belong to registered compiler profiles or compiled request snapshots.

No compiler may fill a missing canonical fact by rereading RP prose. Unsupported canonical intent must produce an explicit capability error or review requirement, not a guessed request.

## Representative Evidence

| Modality | Documented model inputs | Canonical B-100 implications | Compiler/profile-only inputs | Sources |
|---|---|---|---|---|
| Still image | Subject identity and appearance, frozen action/state, scene/context, composition, camera, lighting/style, dimensions, and typed reference images | Stable subject/location/prop IDs; exact Moment state; wardrobe; pose; visibility; composition and camera intent; continuity references with semantic roles | FLUX structured JSON or prose ordering; Pony tags; SDXL natural language; negative-prompt policy; width/height constraints; seed; guidance/sampler | [FLUX.2 prompting guide](https://docs.bfl.ai/guides/prompting_guide_flux2), [SDXL model card](https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0) |
| Speech / TTS | Exact text, voice, language, delivery/emotion, punctuation or tags, pauses, pronunciation, speed, continuity context, output format, and returned alignment timestamps | Immutable display text plus normalized spoken text; speaker identity; language/locale; delivery and prosody intent; pause/overlap intent; pronunciation lexemes; previous/next cue lineage; requested timing window; persisted realized alignment | Voice/model ID; provider audio tags or SSML; stability/similarity/style/speed mapping; pronunciation dictionary IDs; output codec; seed; normalization switch | [ElevenLabs TTS best practices](https://elevenlabs.io/docs/overview/capabilities/text-to-speech/best-practices), [speech with timing API](https://elevenlabs.io/docs/api-reference/text-to-speech/convert-with-timestamps) |
| Sound effects / ambience | Ordered event description, duration, loop behavior, prompt influence, and distinct effect/ambience semantics | Typed cue role; ordered source-grounded events; start/end window; duration intent; loopability; diegetic/spatial source; intensity envelope; continuity group; mix role | Prompt-influence/adherence values; provider duration bounds; output format; seed | [ElevenLabs sound-effects API](https://elevenlabs.io/docs/api-reference/text-to-sound-effects/convert), [sound-effects overview](https://elevenlabs.io/docs/overview/capabilities/sound-effects) |
| Music | Genre, mood, instrumentation, BPM, key, production era/style, ordered arrangement, sections and durations, lyrics/instrumental state, references, and conditioning strength | Music intent; tempo/key when authored; instrumentation and mood; ordered section windows; entry/exit/transition intent; vocal/lyric ownership; reference role; loop/stem intent | Provider composition-plan shape; positive/negative style lists; conditioning strength; inpainting song/range IDs; model ID; output format; seed | [ElevenLabs music best practices](https://elevenlabs.io/docs/overview/capabilities/music/best-practices), [detailed composition API](https://elevenlabs.io/docs/api-reference/music/compose-detailed) |
| Generated video | Subject, action sequence, scene/context, camera angle/movement/lens, visual style, pacing/evolution, duration, first/last/reference frames, and optional dialogue/SFX/ambience/native audio | Coverage window; ordered action phases; visual start/end/internal states; camera and motion intent; pacing; typed visual references; duration intent; dialogue/sound/music cue links; explicit audio ownership | Model duration/ratio/resolution limits; first/last-frame transport; provider negative prompt; prompt rewriter; source media upload shape; generation seed | [Google Veo prompt guide](https://docs.cloud.google.com/vertex-ai/generative-ai/docs/video/video-gen-prompt-guide), [Runway API models](https://docs.dev.runwayml.com/guides/models/) |
| Lip-sync / performance | One visual input plus audio or text, segment windows, audio crop windows, target face, duration mismatch policy, expression scope/emotion, clean single-speaker audio, visible face, and suitable speaking motion | Lip-sync segment linked to dialogue cue and realized speech asset; target character; exact video and audio windows; speaker selection intent; duration-fit policy; face-visibility requirement; expression/head-motion intent; source visual role; sync review status | Sync model; URL/asset transport; pixel/frame face coordinates or bounding boxes; `sync_mode` encoding; model edit region; provider emotion enum; temperature; occlusion option; codec/FPS/resolution limits | [Sync create-generation API](https://sync.so/docs/api-reference/api/generate-api/create.md), [segments guide](https://sync.so/docs/developer-guides/segments.md), [speaker selection](https://sync.so/docs/developer-guides/speaker-selection.md), [sync mode](https://sync.so/docs/developer-guides/sync-mode.md), [media requirements](https://sync.so/docs/compatibility-and-tips/improving-lip-sync-quality.md), [react-1](https://sync.so/docs/models/react.md) |

## Required Canonical Ontology

The evidence requires these provider-neutral concepts before compiler implementation:

1. **Shared timebase:** Beat-relative seconds plus ordered event anchors; frame indexes are derived only after a target FPS exists.
2. **Typed windows:** event, dialogue, ambience, effect, music section, video coverage, and lip-sync windows have explicit start/end anchors and duration intent.
3. **Stable identities:** characters, speakers, locations, wardrobe states, and props use authoritative IDs or application-resolved compact keys.
4. **Dual dialogue representation:** immutable display/source text is separate from normalized spoken text; normalization cannot alter the displayed script.
5. **Performance intent:** emotion, intensity, pace, pause, overlap/interruption, accent/language, pronunciation, facial-expression scope, and head-motion intent are explicit where source-supported.
6. **Visual state sequence:** video coverage names exact start, end, and optional internal Moment states plus the allowed ordered changes between them.
7. **Typed references:** every image, video, audio, voice, pose, style, identity, location, or continuity reference declares its role and lineage.
8. **Audio ownership:** each coverage plan states whether dialogue, ambience, effects, and music are external, generated natively with video, hybrid, or intentionally absent.
9. **Music structure:** music is a duration-bearing ordered section plan when structure matters, not a single mood string.
10. **Realized alignment:** generated speech stores audio duration and character/word timing so video, lip-sync, captions, and B-101 placement use the actual take rather than estimated timing.

## Cross-Modal Consistency Invariants

- A character ID resolves to the same appearance, wardrobe, voice identity, and target face across all requests in one lineage.
- A Moment image and a video keyframe describe the same frozen cast, location, props, pose, lighting, and camera state.
- A video action begins at its start Moment, follows Beat event order, and ends at its end Moment without inventing intermediate story events.
- Spoken text preserves the authored words; normalized text may change pronunciation representation only.
- Dialogue speaker, video-visible speaker, native-video dialogue attribution, and lip-sync target are the same canonical character unless an explicit off-screen policy says otherwise.
- Sound effects occur inside their owning event windows; ambience and music continuity do not reset at compiler boundaries unless the plan declares a transition.
- Requested durations are reconciled before generation. A duration mismatch policy is explicit and may not silently truncate dialogue or retime story events.
- Native-video audio and externally generated audio compile from the same cue set. Native generation does not gain permission to invent dialogue, effects, or music.
- Provider capability loss is visible. A compiler may omit only canonically optional intent and must report every unsupported required intent.

## Golden Compilation Fixture

Before freezing the acceptance corpus, one representative Beat lineage must compile into all of the following without mutation or RP-text rereading:

1. Pony tag prompt and request.
2. SDXL/Juggernaut natural-language prompt and request.
3. FLUX-like structured image request with typed references.
4. TTS request plus expected realized-alignment import shape.
5. Separate ambience and ordered sound-effect requests.
6. Duration-bearing music composition plan.
7. `MomentHold`, `MomentAction`, `MomentTransition`, `BeatExcerpt`, and `WholeBeat` video requests.
8. Native-audio video request using the same dialogue/sound/music cue IDs.
9. Lip-sync/performance request using an approved visual asset, realized speech asset, exact segment windows, and target character.

The fixture asserts identity, appearance, wardrobe, location, props, frozen state, action order, dialogue, emotion, timing, camera, ambience, effects, and music consistency across every compiled artifact.

## Evidence Maintenance

Every compiler epic must update this matrix with the exact provider/model version, required/optional/unsupported fields, source URL, and verification date before implementation. Documentation claims are sufficient for ontology design; production support additionally requires captured request/response fixtures against the configured model.