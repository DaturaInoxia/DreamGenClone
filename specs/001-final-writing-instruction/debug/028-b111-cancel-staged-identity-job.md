# B-111 Cancel Staged Identity Job

## Report

On 2026-09-06, the Production Studio route for session `4f2eec18-b190-4beb-ad35-8d520ae5c800`, interaction `dad139a5-8e98-469b-ab74-8e55404f1732`, and Moment enrichment `e2a8838a-bd13-4f7a-bf2c-df6cd728ee78` showed a pending composition with no visible running job. The user requested cancellation.

## Analysis

The exact Omniscient production group is `3004e8c0-bb83-4c2a-8cb8-d1b7d1e6934b`. Its composition attempts are complete. The actual pending record is Identity image `0daa2b02-8d72-4579-b2c9-cade62714644`, created at `2026-09-06T17:10:21Z`, with staged durable job `05fae8f89c21434c959f3bd8fe008ebd` (`scene-image-editing`, lane `ImageEdit`, attempt `0/1`).

The job has never started. The durable repository's normal cancel transition does not currently include `Staged`; cancelling that queue row alone would leave the image record permanently `Pending`, because no worker had claimed it.

## Plan

1. Cancel the verified staged job with an ID-and-status constrained mutation.
2. Mark the exact matching pending Identity image failed with an explicit cancellation reason.
3. Re-read the trace and confirm no other job or image was changed.

## Resolution

- Applied `b111-cancel-staged-identity-job.sql`: exactly one row changed. Job `05fae8f89c21434c959f3bd8fe008ebd` is now `Cancelled` with no retry or lease.
- Applied `b111-resolve-cancelled-identity-image.sql`: exactly one row changed. Image `0daa2b02-8d72-4579-b2c9-cade62714644` is now `Failed` with `Cancelled before the staged durable Identity job was started.`

## Validated

- [x] Post-mutation trace confirms the target job is `Cancelled` and the target image is `Failed`.
- [x] The previously reported pending item was an Identity edit, not a Composition render. The group’s composition attempts remain complete.
- [x] No other durable job was modified.