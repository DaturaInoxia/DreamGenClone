-- The character LoRAs that exist, and whether the checkpoint each was trained on is one the Playground can select.
-- The picker offers an artifact only when its BaseModelId equals the selected model's ModelIdentifier.
SELECT
    a.Id                                                                  AS ArtifactId,
    a.CharacterProfileId                                                  AS Character,
    a.Version                                                             AS Version,
    a.Status                                                              AS Status,
    a.BaseModelId                                                         AS TrainedOn,
    a.BaseModelVersion                                                    AS BaseVersion,
    json_extract(a.PayloadJson, '$.triggerToken')                         AS Trigger,
    CASE WHEN m.Id IS NULL THEN 'NO REGISTERED MODEL HAS THIS CHECKPOINT'
         ELSE 'selectable: ' || m.DisplayName || ' (' || m.ModelIdentifier || ')'
    END                                                                   AS PickerOutcome
FROM CharacterLoraArtifacts a
LEFT JOIN RegisteredModels m
       ON m.ModelIdentifier = a.BaseModelId
      AND m.IsEnabled = 1
ORDER BY a.Status, a.BaseModelId;
