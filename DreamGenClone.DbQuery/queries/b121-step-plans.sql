SELECT p.Kind, p.OrderIndex, p.Step, p.HandlerKey, p.TemplateKey
FROM CharacterIdentityStepPlans p
ORDER BY p.Kind, p.OrderIndex;
