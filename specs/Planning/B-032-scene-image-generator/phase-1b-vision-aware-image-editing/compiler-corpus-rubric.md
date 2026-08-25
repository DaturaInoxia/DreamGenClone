# Phase 1B Compiler Corpus Rubric

**Frozen:** 2026-08-25
**Corpus:** [`compiler-corpus.json`](compiler-corpus.json)

The corpus is executed once with the pinned Qwen2.5-VL 7B AWQ artifact, served model identity
`qwen2.5-vl-7b-edit-compiler`, compiler schema `scene-image-edit-compiler-v1`, system prompt
`qwen-edit-rules-v1`, temperature `0`, top-p `1`, and maximum output of 1,024 tokens. There are no
retries, alternate models, alternate providers, or text-only requests.

## Automatic Gates

- **Source integrity:** source byte count and SHA-256 must exactly match the manifest.
- **Model identity:** every response must name the exact served model.
- **Schema validity:** all ten responses must contain exactly the versioned application fields and
  satisfy terminal-state constraints.
- **Status accuracy:** all ten statuses must exactly match the frozen expected status.
- **Target accuracy:** every ready case must contain all required visible locator terms across its
  target locators. All six ready cases must pass.
- **Ambiguity precision:** both ambiguous requests must return `clarification_required` without an
  executable prompt.
- **Invention:** forbidden names, relationships, unseen actors, and hidden story facts must not
  appear in source summaries, locators, or compiled prompts. Zero inventions are allowed.
- **Latency:** every request must complete within 90 seconds.

## Human Review

For each ready case, confirm that the compiled prompt identifies only visible targets, requests only
the intended change, and preserves unaffected people, identity, clothing not selected for change,
pose, composition, lighting, and background. Clarification cases must ask a useful question without
guessing. Invalid cases must explain that the requested target or fact is not visibly available.

The candidate passes P1B-010 only when every automatic gate passes and the human review records no
targeting error, unsupported invention, or inappropriate refusal of a supported non-explicit edit.
Permitted adult-analysis acceptance is separate and is not inferred from this corpus.