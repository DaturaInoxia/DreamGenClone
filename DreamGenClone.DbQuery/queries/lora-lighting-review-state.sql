-- Read-only: which model the LoRA cells render on, and whether any attempt images exist.
SELECT 'lora-cell-model' AS What,
       COALESCE((SELECT group_concat(COALESCE(CharacterProfileId, 'global') || '=' || COALESCE(LoraCellModelId, 'NULL'), ' | ')
                 FROM ReferenceWorkflowSettings), 'no-rows') AS Detail
UNION ALL
SELECT 'cell-attempts', CAST(COUNT(*) AS TEXT) FROM SceneAssetImages WHERE CandidateBatchId LIKE 'lora-cell-%'
UNION ALL
SELECT 'datasets', CAST(COUNT(*) AS TEXT) FROM CharacterLoraDatasets
UNION ALL
SELECT 'approved-packs-for-becky',
       COALESCE((SELECT group_concat(Version || ' | scope=' || PackScope || ' | status=' || Status
                                    || ' | face=' || COALESCE(CanonicalFaceAssetId, 'none')
                                    || ' | body=' || COALESCE(CanonicalFullBodyAssetId, 'none'), CHAR(10))
                 FROM CharacterImageIdentityPacks
                 WHERE CharacterProfileId = (SELECT CharacterProfileId FROM CharacterLoraDatasets LIMIT 1)), 'none');
