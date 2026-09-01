# B-100 Frozen Corpus

This fixture set contains eight invented, sanitized, non-explicit roleplay turns. IDs and UTC timestamps are stable. It contains no production sessions, provider configuration, credentials, prompts, or copied user prose.

## Cases

| Case | Coverage |
|---|---|
| `solo-workshop` | Single actor and compact action sequence |
| `ensemble-kitchen` | Multiple co-located actors |
| `parallel-viewpoints` | Concurrent observations from different viewpoints |
| `remote-observer` | Off-location evidence and participant visibility |
| `location-transition` | Explicit movement between locations |
| `clothing-transition` | Wardrobe continuity change |
| `long-complex-turn` | Long turn with several candidate moments |
| `missing-narrative` | Canonical malformed-input rejection before model invocation |

Every valid case defines reviewed Beat evidence, 2-4 Moment candidates, a selected recommendation, required roles, and required source-fact interaction IDs. `missing-narrative` instead declares the expected `corpus_narrative_missing` preflight code and is excluded from stage-validity denominators.

The loader rejects unknown JSON properties, duplicate IDs, unstable file traversal, path escape, missing expectations, and malformed source references. The report records a SHA-256 checksum over the manifest paths and fixture bytes so results identify the exact corpus revision.
