---
applyTo: 'DreamGenClone.Infrastructure/Models/CompletionClient.cs,DreamGenClone.Tests/RolePlay/CompletionClient*.cs,DreamGenClone.Web/Components/Pages/PromptTester.razor,DreamGenClone.Web/Components/Pages/ModelManager.razor,DreamGenClone.Web/Application/ModelManager/**/*.cs,DreamGenClone.Domain/ModelManager/**/*.cs,specs/model-evaluation/**'
description: 'Verified model behaviour for the hosted providers (Together AI and DeepSeek direct): which models refuse or sanitise explicit content, per-model context/output ceilings, the three different thinking-control shapes, and the real cost/speed trade-off. Read before changing model resolution, thinking control, Prompt Tester, or Model Manager defaults.'
---

# Model Evaluation — Verified Findings (2026-10-27)

**Full evidence package: [`specs/model-evaluation/README.md`](../../specs/model-evaluation/README.md)** (prompts, tables, samples, runners).
This file is the short version for agents editing model plumbing.

## Thinking control has three shapes — verify, never assume

| Provider / model | Parameter | Notes |
|---|---|---|
| Together, most models | `chat_template_kwargs: {"thinking": bool}` | Verified: reasoning drops to zero. Together also documents `reasoning: {"enabled": bool}` for hybrids. |
| DeepSeek direct (`api.deepseek.com`) | `{"thinking": {"type": "enabled"\|"disabled"}}` | **Ignores `chat_template_kwargs`.** Thinking is ON by default, effort `high`. |
| gpt-oss-120b | `reasoning_effort: low\|medium\|high` | Adjustable effort rather than on/off. |

`CompletionClient` routes by provider base URL (`UsesDeepSeekThinkingApi`). If a new provider appears, add its
shape there — an ignored thinking parameter is indistinguishable from a working one in the response body.

**Verify a toggle by observation:** send a reasoning-inducing prompt twice, once with the toggle off, and compare
`reasoning_content` length. A model whose reasoning does not change is an *always-on* model and must NOT be flagged
`SupportsThinkingControl` in Model Manager, or the UI offers a switch that does nothing.

## Which models will/will not write explicit prose

Verified by reading each sample (not keyword matching):

- **Write hardcore prose:** `zai-org/GLM-5.3-Flash` (best value), `GLM-5.2`, `DeepSeek-V4.1-Flash` and
  `-1017`, `DeepSeek-V4-Pro-0813` (Together and direct), `Muse-Glimmer-30B`, `thinkingmachines/Inkling`
  (explicit act and climax but euphemistic anatomy — passes a keyword filter).
- **Refuse, then offer softcore as a substitute:** `Qwen/Qwen3.8-Flash`, `meta-models/Muse-Spark-Open-816B-A42B`,
  `MiniMaxAI/MiniMax-M3.1-Flash`. These are refusals, not compliance — do not count them as working models.
- **Refuse outright:** `openai/gpt-oss-120b`, `MiniMaxAI/MiniMax-M3`, `Qwen/Qwen3.5-9B`, `Qwen/Qwen3.7-Plus`,
  `zai-org/GLM-5.3`, plus unstable: `Nemotron-3.5-Super-VL`, `Qwen/Qwen3.6-Plus`.

## Budget and limits are per model — never a shared default

- `MaximumContextTokens` / `MaximumOutputTokens` are per-model columns on `RegisteredModels`; both are now filled
  from the endpoint's own answer. Ceilings range from 65,536 (`Qwen3.6-Plus`, `Muse-Glimmer`) to 393,216
  (DeepSeek direct).
- A reasoning model at a small `max_tokens` spends the whole budget thinking and returns **empty content**. That
  looks exactly like a refusal. Give at least ~2,000 tokens before judging any reasoning model.
- `GET /v1/models` reports context and price but **not** max output; discover it by sending a large `max_tokens`
  and reading the provider's rejection (e.g. `Range of max_tokens should be [1, 65536]`).

## Cost: the only structural difference is the off-peak window

Together and DeepSeek charge **identical per-token prices at peak** for the same model. DeepSeek's off-peak
(16:30–00:30 UTC) is exactly 50% off. Per-word differences measured in one sample are token-count noise, not host
pricing — do not budget from a single sample.

- V4 Pro → **Together** (2.7× faster; off-peak discount does not cover the latency).
- V4 Flash → **Together at peak, DeepSeek direct off-peak**.

## Streaming aborts are not moderation

Together sometimes ends a stream with `{"error":{"code":"managed_chat_error"}}`, no `[DONE]` and no
`finish_reason`. `CompletionClient` raises `IncompleteCompletionException` with the partial text;
Prompt Tester shows the partial prose plus the provider message. Do not "fix" this by retrying as a policy issue —
it correlates with long reasoning streams, not with content.

## Prompt Tester behaviour worth preserving

- Changing the model in the dropdown resets sampling defaults, thinking mode and the displayed result
  (`SelectedModelKey` → `ApplyModelDefaults`). Without it, one model's settings silently applied to the next.
- The model list is filtered to `ModelKind.Text`; image models can never answer a chat request.
- Saved runs persist content only — reasoning lives in the result pane, not in `PromptTestRuns`.
