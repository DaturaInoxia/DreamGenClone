-- The Campground Intimacy scenario's Locations and Setting.EnvironmentalDetails, plus the cast, as the
-- scenario editor holds them. Probes both casings because the payload's property names depend on the
-- serializer the repository uses.
SELECT
    json_type(PayloadJson, '$.Locations')            AS LocationsPascal,
    json_type(PayloadJson, '$.locations')            AS LocationsCamel,
    json_type(PayloadJson, '$.Setting')              AS SettingPascal,
    json_type(PayloadJson, '$.setting')              AS SettingCamel,
    length(PayloadJson)                              AS PayloadBytes
FROM Scenarios
WHERE Id = '135a9237-bcc1-45ee-afa1-ff2f14c49e50';
