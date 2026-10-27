import sqlite3

DB = r'D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db'
con = sqlite3.connect(f'file:{DB}?mode=ro', uri=True)
con.row_factory = sqlite3.Row

for p in con.execute("select Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, TimeoutSeconds, "
                     "length(ApiKeyEncrypted) as keylen, ImageProtocol, IsEnabled "
                     "from Providers where Name like '%eep%'"):
    print(dict(p))

print('\nmodels:')
for m in con.execute("""select m.ModelIdentifier, m.DisplayName, m.IsEnabled, m.SupportsThinkingControl,
                               m.MaximumContextTokens, m.MaximumOutputTokens, m.ModelKind
                        from RegisteredModels m join Providers p on p.Id = m.ProviderId
                        where p.Name like '%eep%'"""):
    print(dict(m))
