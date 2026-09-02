# Debug 030 - Validate image-model parameter support

**Report**

- Date: 2026-08-22
- Symptom: Scene Image rendering needs native image API controls, but the Model Manager only tested basic image endpoint reachability.
- Risk: A provider/model could appear healthy while rejecting or ignoring `negative_prompt` or `disable_safety_checker`.

**Analysis**

- `ImageGenerationClient.CheckImageModelHealthAsync` sent only a minimal image request with model, prompt, dimensions, and base64 response format.
- TogetherAI documents `negative_prompt` and `disable_safety_checker` at the image endpoint level, but optional parameter support varies by model.
- The application resolves one function-default image model and has no ordered fallback configuration. Health-check results are persisted for display but are not an automatic fallback policy.
- Model Manager provider content policy is the configured source for whether the adult safety-checker probe is applicable.

**Plan**

1. Extend image-model connection testing to send a basic probe, a `negative_prompt` probe, and, for adult-allowed providers, a `disable_safety_checker` probe.
2. Omit optional JSON properties when not being tested so null values are never sent to providers.
3. Report the first rejected capability explicitly in the Model Manager result.
4. Preserve the existing basic reachability and text-model test paths.
5. Cover the request sequence with focused tests and run the complete test suite.

**Resolution**

- Added `ImageContentPolicy` to the image health-check contract.
- Added separate capability probes for `negative_prompt` and `disable_safety_checker`.
- Added nullable optional request properties with null omission.
- Updated the Model Manager service to pass the configured provider content policy.
- Updated image-client tests to verify all three request payloads.

**Validated**

- [x] Image generation client tests: 9 passed, 0 failed.
- [ ] All Scene Image tests.
- [ ] Full test suite.
- [ ] Full solution build.
- [ ] Automatic model switching remains pending a configured ordered fallback policy.
