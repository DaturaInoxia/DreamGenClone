---
description: "Use when checking RunPod pod status, updating pods, handling 'pod migrated', synchronizing migrated pod IDs, or updating Model Manager RunPod endpoints."
applyTo: "helpers/runpod/**,DreamGenClone.DbQuery/**"
---

# RunPod Pod Migration

- Never create, terminate, or delete a pod as part of migration endpoint synchronization.
- Attempt to start the manifest pod when requested.
- When RunPod reports unavailable GPU capacity, tell the user to use RunPod's Migrate action.
- After the user says the pod migrated, list account pods and locate exactly one `RUNNING` successor:
  - Its ID differs from the manifest pod ID.
  - Its image matches `containerImage`.
  - Its name equals `podName` or starts with `<podName>-migration`.
- Trust RunPod's `RUNNING` status and exposed manifest HTTP port for a migrated successor. Do not run readiness, model-presence, or model-identity probes against a migrated pod.
- Derive the endpoint as `https://<podId>-<inferencePort>.proxy.runpod.net`.
- Update Model Manager only when the endpoint differs, using the guarded `provider-endpoint-update` command.
- Do not overwrite a concurrent Model Manager endpoint change.
- If no dedicated provider exists, create or split one explicitly; never repoint a provider shared by incompatible models without user approval.
- Report old pod ID, migrated pod ID, status, provider ID, old URL, and new URL.

Use the `runpod-pod-migration` skill for the complete operational procedure.