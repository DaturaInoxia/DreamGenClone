SELECT Id, Name, BaseUrl, ApiKeyEncrypted IS NOT NULL AS HasApiKey
FROM Providers
WHERE Name IN (
	'RunPod Qwen Image Edit',
	'RunPod Juggernaut Serverless',
	'RunPod Serverless BigLust'
)
ORDER BY Name;