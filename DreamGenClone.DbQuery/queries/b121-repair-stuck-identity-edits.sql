-- Debug record 066 data repair: the three Identity-stage edit rows whose render DID succeed (bytes were
-- written to disk) but whose completion was refused by the claim-guarded transition before the fix.
-- Values are the real ones: SHA-256/pixel size read from the stored PNG, model/provider/policy from the
-- only enabled registered model carrying that identifier, StartedUtc = the enqueue that immediately began
-- the run, CompletedUtc = the moment the bytes were stored.
UPDATE SceneImages
SET Status = 'Complete',
    FileRelativePath = '8bc36efb-b235-485b-8675-b98ad7754e59/' || Id || '.png',
    ModelIdentifier = 'qwen_image_edit_2511_fp8mixed.safetensors',
    ProviderName = 'Local ComfyUI (WOOD-GAME-MAIN 5080)',
    ContentPolicy = 'AdultAllowed',
    ImageSize = '1024x1024',
    Sha256 = CASE Id
        WHEN '95a3cadd-3825-44ef-88f5-846bfaccf45d' THEN 'E7FA453427A42799CD866C77BDF5EF28E027082D6D1483051476D45081F64BC4'
        WHEN '6bae2aa0-9086-4680-b1b8-736c32bdff97' THEN '0A3A4BFCBEBABEAED600EDE17CA066A9901A680A32382B8732528156360E1CBC'
        WHEN 'e335f53e-2b4b-4caf-b079-30c9e44639d5' THEN 'FA39E58219F513E3C7EB8BE7EB3ED48DD71E7AEE4E6CCEA23BA2C8C06DBEDAD3'
    END,
    StartedUtc = CreatedUtc,
    CompletedUtc = CASE Id
        WHEN '95a3cadd-3825-44ef-88f5-846bfaccf45d' THEN '2026-09-22T02:51:18.2082711Z'
        WHEN '6bae2aa0-9086-4680-b1b8-736c32bdff97' THEN '2026-09-22T02:35:00.3088632Z'
        WHEN 'e335f53e-2b4b-4caf-b079-30c9e44639d5' THEN '2026-09-22T01:31:52.0147312Z'
    END,
    UpdatedUtc = CASE Id
        WHEN '95a3cadd-3825-44ef-88f5-846bfaccf45d' THEN '2026-09-22T02:51:18.2082711Z'
        WHEN '6bae2aa0-9086-4680-b1b8-736c32bdff97' THEN '2026-09-22T02:35:00.3088632Z'
        WHEN 'e335f53e-2b4b-4caf-b079-30c9e44639d5' THEN '2026-09-22T01:31:52.0147312Z'
    END,
    ErrorMessage = NULL
WHERE Id IN (
        '95a3cadd-3825-44ef-88f5-846bfaccf45d',
        '6bae2aa0-9086-4680-b1b8-736c32bdff97',
        'e335f53e-2b4b-4caf-b079-30c9e44639d5')
  AND Status = 'Pending'
  AND Operation = 'Edit';
