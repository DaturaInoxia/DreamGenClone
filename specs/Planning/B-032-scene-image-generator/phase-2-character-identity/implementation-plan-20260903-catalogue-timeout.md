# Implementation Plan - Beat Catalogue Timeout and Recovery

**Date:** 2026-09-03
**Source test:** P2-MAN-090A
**Defect:** BUG-20260903-001

## Findings

- Catalogue `345d22e6-c6b9-40a5-bfef-3ac4a40cbcd6` remained `Processing` for approximately 13 minutes.
- Attempt `8c686b53-4203-4088-810a-09a4cadf58cc` and durable job `83c0f7d4-85ff-47b2-9a9d-99909038dd0c` remained `Processing`, attempt 1 of 3.
- The configured provider timeout was 240 seconds, but the completion client used `ResponseHeadersRead` and did not apply a cancellation deadline to response-body deserialization.
- The durable lease was 120 seconds and was actively renewed while the handler waited.
- Expired leases were recovered only by startup recovery; no live sweep existed.
- The Studio exposed only a spinner and no elapsed time, timeout, heartbeat, provider state, or retry information.
- Product note: add a System Menu screen for durable job history and status, including job type/lane, current state, elapsed time, attempt history, retry reason, lease/heartbeat timestamps, provider/model, and terminal diagnostics.

## Changes applied

1. Bound structured completion request headers and response-body parsing to the resolved provider timeout in `OpenAiStructuredTextCompletionClient`.
2. Added continuous expired-lease recovery to `TextAnalysisDurableWorker` using the configured lease interval.
3. Added a durable executor watchdog using the resolved model's persisted provider timeout, converting an over-timeout handler into the existing transient retry/failure path.
4. Recorded the exact test IDs, persisted statuses, configuration, and observed defect in `manual-test-runner.md`.

## Remaining validation

- Add or strengthen a regression test where response headers arrive but the body never completes, proving the configured timeout cancels the operation.
- Add a worker-level test proving expired leases are recovered without an application restart.
- Verify timeout classification reaches durable retry/failure and updates catalogue/attempt status. Covered by executor regression tests (10/10 focused; 29/29 affected suite).
- Add Studio status fields for elapsed time, last update, timeout/retry state, and terminal diagnostics.
- Add the System Menu job-history screen as the cross-workflow view for durable job status and attempt history.
- Re-run P2-MAN-090A after a clean Development restart; verify the two existing Processing jobs are recovered by the normal path and no duplicate provider request is created.

## Verified follow-up

- The current B1 version 2 job completed successfully at `2026-09-03T20:24:17.5249376Z` after
	273,061 ms. The durable job, attempt, and plan all reached `Complete`; this was slow provider
	latency below the configured 600-second timeout, not a lost completion.
- The newer preserved job completed after recovery/retry with a 46,691 ms duration and a 2,740-byte response.
- The older preserved job reached attempt 3 of 3 and failed with the persisted `scene_beat_output_invalid` validation diagnostic.
- Both durable records were retained. No duplicate catalogue was created by recovery.

## Blast radius

The completion timeout affects structured text calls used by the Beat Catalogue and related analysis
stages. Live lease sweeping affects all durable lanes but only transitions rows whose lease is already
expired. No model, provider, strategy, or RP narrative fallback is introduced.