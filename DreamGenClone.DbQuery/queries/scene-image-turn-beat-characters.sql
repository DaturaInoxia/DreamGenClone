WITH selected_analysis AS (
    SELECT Id, BeatsJson
    FROM SceneImageBeatAnalyses
    WHERE AnchorInteractionId = '{{id}}'
      AND Status = 'Complete'
    ORDER BY UpdatedUtc DESC
    LIMIT 1
)
SELECT
    selected_analysis.Id AS AnalysisId,
    json_extract(beat.value, '$.beatId') AS BeatId,
    json_extract(beat.value, '$.label') AS BeatLabel,
    json_extract(beat.value, '$.location') AS BeatLocation,
    json_extract(character.value, '$.name') AS CharacterName,
    json_extract(character.value, '$.involvement') AS Involvement,
    json_extract(character.value, '$.physicalLocation') AS PhysicalLocation,
    json_extract(character.value, '$.position') AS Position,
    json_extract(character.value, '$.actionOrObservation') AS ActionOrObservation,
    json_extract(character.value, '$.sightline') AS Sightline,
    json_extract(character.value, '$.visibleCharacterNames') AS VisibleCharacterNames,
    json_extract(character.value, '$.clothing') AS Clothing
FROM selected_analysis
JOIN json_each(selected_analysis.BeatsJson) AS beat
JOIN json_each(json_extract(beat.value, '$.characters')) AS character
ORDER BY CAST(json_extract(beat.value, '$.order') AS INTEGER), character.key;
