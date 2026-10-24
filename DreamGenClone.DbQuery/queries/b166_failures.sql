SELECT Id, Status, substr(ErrorMessage,1,1200) AS Err, substr(OutputPrompt,1,200) AS Prompt, length(OutputPrompt) AS Len, CreatedUtc
FROM SceneImagePromptRecords
WHERE SessionId = '8bc36efb-b235-485b-8675-b98ad7754e59'
ORDER BY CreatedUtc DESC
LIMIT 8;
