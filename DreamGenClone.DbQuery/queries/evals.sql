SELECT *
FROM RolePlayV2CandidateEvaluations
WHERE SessionId = '{{id}}'
ORDER BY EvaluatedUtc DESC;