# 044 - Moment enrichment HTTP 400 on glm-4.7/OpenRouter (reasoning_effort 'none'), resolved by model config

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59` · Interaction `d85cbd67-53a1-47b1-b5c5-40c34a6da180`
- **Catalogue**: `fccc6dc8-fc1a-4bd2-841e-860a748d47f3`
- **Error**: `structured_text_http_400` — "Structured text completion failed for provider 'OpenRouter' with HTTP 400."
- **Date**: 2026-09-08 · follows `043-moment-enrichment-video-roles-exact-match.md`

## Report

After debug 043 (v2 schema fix) the enrichment was re-run on the SAME glm-4.7 /
OpenRouter config and failed with instant HTTP 400 (0.7s) — r2/r3/r4 (three durable
retries) all failed identically. User hypothesis: the 043 schema change caused it.

## Analysis

OpenRouter 400 body (log line 9035, 10:02:30 local) shows the request was routed to
three upstreams that ALL failed:
- Google → `Expected 'reasoning_effort' to be one of: 'high','low','max','medium','minimal'; found 'none'.`
- DeepInfra → 429 rate-limited (proves the request INCLUDING the v2 schema was
  accepted — a schema error cannot produce a rate-limit response).
- AtlasCloud → 400 `invalid request params`.

Root cause: the beat-analyzer structured completion client sends
`reasoning: {effort: "none"}` whenever `ThinkingMode = Disabled`. glm-4.7 is
configured Disabled (it has `SupportsThinkingControl=0`, so Enabled is rejected;
Default is rejected by policy for the beat analyzer). OpenRouter now routes glm-4.7
to Google's backend, which rejects `reasoning_effort="none"`. Same-day evidence that
this is NOT schema-dependent and predates 043: the identical 400 hit beat-production
schemas (untouched by 043) at 00:22/01:20/01:22 local, while the identical
reasoning=none request succeeded at 05:36-05:48 local when routed to a tolerant
upstream. The 8-hour idle gap (01:48 → 10:02) is when glm-4.7 lost a working upstream
for disabled-thinking requests.

## Resolution

No code change. Confirmed via live DB: m1 revisions 1-4 (glm-4.7/OpenRouter) all
failed (r1 output_invalid on v1; r2-r4 HTTP 400 on v2); m1 revision 5 assigned to
**deepseek-v4-flash / DeepSeek** (Model Manager config change of
`RolePlaySceneBeatAnalyzer`) **Completed** at 14:15:13Z with
`videoKeyState = {"roles":["VideoStart"]}` — proving the 043 v2 schema is correct and
the failure was provider routing.

Also verified the per-model mode rules in `SceneBeatAnalyzerResolver`:
- Default (0): rejected for the beat analyzer (explicit mode required).
- Enabled (1): allowed only when the assigned model `SupportsThinkingControl`
  (DeepSeek = 1 → valid).
- Disabled (2): always allowed (only legal value for glm-4.7 since
  `SupportsThinkingControl=0`).

## Follow-up options (not taken)

- If glm-4.7 / OpenRouter is ever needed again for disabled thinking, the client
  (`OpenAiStructuredTextCompletionClient`) should OMIT the `reasoning` field when
  `ThinkingMode.Disabled` instead of sending `effort:"none"` (Default-mode glm calls
  already omit it and work all day). Small code change + one test update
  (`StructuredTextCompletionClientTests` asserts `effort=="none"`).
- Note: switching `RolePlaySceneBeatAnalyzer` to DeepSeek applies to the WHOLE
  scene-beat pipeline (catalogue, moment discovery, beat production, enrichment) —
  earlier catalogue/beat rows were produced by glm-4.7; later pipeline stages now run
  on DeepSeek flash.

## Validated

- [x] Live DB: m1 r5 Complete on deepseek-v4-flash with exact v2 video role set.
- [x] 043 schema regression tests already green (26 enrichment tests).
- [ ] No code change in this round; no build/test required.
