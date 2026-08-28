SELECT
    rm.DisplayName,
    rm.ModelIdentifier,
    rm.IsEnabled,
    rm.ModelKind,
    GROUP_CONCAT(fmd.FunctionName, ', ') AS DefaultForFunctions
FROM RegisteredModels AS rm
LEFT JOIN FunctionModelDefaults AS fmd ON fmd.ModelId = rm.Id
WHERE rm.ModelKind = 1
GROUP BY rm.Id, rm.DisplayName, rm.ModelIdentifier, rm.IsEnabled, rm.ModelKind
ORDER BY rm.DisplayName;
