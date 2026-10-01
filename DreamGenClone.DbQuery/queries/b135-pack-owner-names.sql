-- Names for the two characters that hold an approved identity pack. A pack's CharacterProfileId is a TEMPLATE id
-- (B-127: packs belong to the template), so this reads Templates rather than CharacterProfiles.
SELECT t.Id, t.Name, t.TemplateType
FROM Templates t
WHERE t.Id IN (
    'a4894571-2513-4063-9e16-ef8c4f8134ed',
    'de351eb3-69d3-421a-a762-79ae8ee183ed');
