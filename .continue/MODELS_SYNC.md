# Continue Model Sync

This setup lets you generate the `models:` list in `.continue/config.yml` from a source JSON file.

## Files

- `.continue/config.yml`: contains the managed block between markers.
- `.continue/models.source.json`: your source model catalog.
- `helpers/sync-continue-models.ps1`: sync script.

## Source JSON shape

Use either:

1. An object with `models` array.
2. A raw array of model objects.

Each model supports:

- `name` or `displayName` (optional)
- `provider` (optional, defaults to `openrouter`)
- `model` or `id` or `modelId` (required)
- `enabled` (optional, defaults to `true`)
- `roles` (optional, defaults to `chat`, `edit`)
- `temperature` (optional, defaults to `0.2`)
- `maxTokens` or `maxOutputTokens` (optional, defaults to `8000`)
- `apiKey` (optional, defaults to `${OPENROUTER_API_KEY}`)

## Run sync

From repo root:

```powershell
./helpers/sync-continue-models.ps1
```

If your exported DB catalog is elsewhere:

```powershell
./helpers/sync-continue-models.ps1 -SourcePath "D:/path/to/your/models-export.json"
```

## Database workflow

1. Export your database model table to JSON.
2. Map fields to the source JSON shape above.
3. Run sync.
4. Reload VS Code window if Continue does not refresh immediately.
