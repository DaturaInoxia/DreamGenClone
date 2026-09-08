# 046 - Beat analyzer back to glm-4.7 + client omits reasoning when thinking Disabled

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59` · Interaction `d85cbd67-53a1-47b1-b5c5-40c34a6da180`
- **Date**: 2026-09-08 · follows `044-moment-enrichment-glm-openrouter-reasoning-effort-400.md` and
  `045-beat-production-cue-global-order-interleaving.md`
- **Decision**: User chose to move `RolePlaySceneBeatAnalyzer` back to GLM-4.7 (option "GLM + client 400 fix").

## Context

- 044 documented glm-4.7/OpenRouter intermittent HTTP 400 from upstreams rejecting
  `reasoning:{effort:"none"}` (sent when Thinking Mode = Disabled) and the fix "not taken":
  omit the `reasoning` block when Disabled.
- 045 (same session) fixed the beat-production `output_invalid` (cue-order) root cause in the
  parser/contract (v4), independent of the model.
- The analyzer was on `deepseek-v4-flash` (MaxTokens=64000) since 044's unblock at ~14:15Z.
  Beat b2/b4 plans were slow (~10 min, 4-pass) and the user wanted GLM back; deepseek remains
  assigned to the fast roleplay semantic functions where it works well.

## Changes

### Code (forward-only)
- `DreamGenClone.Infrastructure/Models/OpenAiStructuredTextCompletionClient.cs` —
  `reasoning` is now always omitted. Previously `ThinkingMode.Disabled` produced
  `reasoning:{effort:"none"}` (OpenRouter glm upstreams reject it → HTTP 400, debug 044).
  Disabled now matches Default (no reasoning block). `Enabled` still sends
  `chat_template_kwargs:{thinking:true}` and never `reasoning`.
- `DreamGenClone.Tests/RolePlay/StructuredTextCompletionClientTests.cs` — first test now asserts
  `reasoning` is absent when Disabled; added
  `GenerateAsync_EnabledThinkingSendsChatTemplateKwargsAndNoReasoning`.

### Config (live dev DB)
- `UPDATE FunctionModelDefaults SET ModelId='0c350f6f-…' WHERE FunctionName='RolePlaySceneBeatAnalyzer'`
  → `OP-glm-4.7` (`z-ai/glm-4.7`, OpenRouter). Kept `MaxTokens=64000`, `ThinkingMode=2 (Disabled)`,
  retries `[5,30]`, MaxConcurrentJobs 3, lease 120. Model row `StructuredOutputMode=1 (StrictJsonSchema)`.
  Resolver gates satisfied (text model, enabled, explicit mode, provider enabled + key present).

## Validated

- `StructuredTextCompletionClientTests` + `SceneBeatAnalyzerResolverTests`: 18 passed / 0 failed.
- Live DB verified: analyzer function default now resolves `OP-glm-4.7` / OpenRouter, key present.
- Webapp rebuilt + restarted (PID from run; HTTP 200 on :5177) so the client fix is live.
- Pending: re-run the failed b2 beat production (plan `57564e46`) — new enqueue snapshots glm
  config; strict-schema + Disabled-thinking glm now omits `reasoning`, so the 044 400 should not
  recur. 045's v4 contract already allows interleaved narration/dialogue ordering.

## Update (20:36Z) — reasoning fix confirmed; glm/OpenRouter upstream failure is different now

Live re-runs on this session's catalogue `c9e43c89`:

- **Reasoning fix confirmed working.** The new OpenRouter 400 body contains NO `reasoning_effort`
  error. It is now: Google `429 rate-limited`, DeepInfra `429 rate-limited`, then OpenRouter falls
  through to **AtlasCloud `400 invalid request params`** (job `ad141510`, b1 v2 `e533da91`,
  failed 1 s after start). glm-4.7/OpenRouter is upstream-flaky again (shared-key rate limits +
  one incompatible fallback upstream) — the same class of day-variance as 044.
- Also observed: b1 v1 (`32543a4d`) failed `structured_text_response_shape_invalid` (glm returned
  an empty 200 on a pass).
- `MaxTokens` for `RolePlaySceneBeatAnalyzer` lowered `64000 → 16000` (config-only) to reduce the
  chance a strict upstream (AtlasCloud) rejects `invalid request params`, since pass outputs only
  reach ~19k chars (~6–8k tokens). Retry pending.
- Note: b2 v4 deepseek (`26aa765e`) failed a DIFFERENT rule — see debug 047
  (`scene_beat_production_output_invalid`: cue source text not verbatim in evidence).

## Notes / future tuning
- glm-4.7/OpenRouter beat runs depend on upstream routing (tolerant upstreams Google/DeepInfra
  recover from rate limits; AtlasCloud currently rejects the request). If AtlasCloud keeps being
  the only fallback and rejects, glm is unusable until routing improves; alternatives: retry
  later, use a direct/TogetherAI GLM, or investigate which param AtlasCloud rejects.
- Splitting beat stages (catalogue / production / moment discovery / enrichment) into separate
  `AppFunction` defaults so each uses its best model remains an open design option (code change).
