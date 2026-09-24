using Microsoft.Data.Sqlite;

/// <summary>
/// B-127: character identity ownership — report (and later apply) the re-key of identity from scenario-instance ids
/// onto character TEMPLATE ids.
///
/// Only <c>preview</c> is implemented here. The applied re-key renames the identity key columns, so it must run in
/// the SAME step as the code switch to template keys (B-127 P0a part 2) — running it earlier would leave an app that
/// cannot read its own stores. The preview is read-only and answers the question the operator needs to see first:
/// which owner id becomes which template, how the pack versions renumber when two per-instance chains merge, and
/// which owners cannot be resolved at all.
///
/// Resolution follows the ONE rule set the running app uses (<c>ICharacterIdentityOwnerResolver</c>): the id is a
/// character template, or a scenario character carrying a <c>TemplateId</c>, or an explicitly linked instance (the
/// scenario link row). Nothing is matched by display name, and nothing is inferred: an owner that cannot be resolved
/// stays unresolved and blocks the apply until a human links it.
/// </summary>
internal static class B127IdentityRekey
{
    /// <summary>
    /// The character-scoped stores whose KEY VALUE is re-keyed (the columns keep their legacy name for now — see
    /// <see cref="DeferredRenameNote"/>). Packs also renumber; the rest only change owner.
    /// </summary>
    private static readonly string[] IdentityTables =
    [
        "CharacterImageIdentityPacks",
        "CharacterIdentityBuilds",
        "CharacterBodyCards",
        "CharacterLoraDatasets",
        "CharacterLoraArtifacts"
    ];

    /// <summary>
    /// Character-scoped tables outside the identity store that key on the character too — prompt overrides and
    /// per-character settings. They move in the same step, otherwise an override would silently stop matching its
    /// character (the resolver would fall back to the global row).
    /// </summary>
    private static readonly string[] CharacterScopedTables =
    [
        "ImageWorkflowPromptTemplates",
        "ReferenceWorkflowSettings"
    ];

    /// <summary>
    /// The physical column is still called <c>CharacterProfileId</c> while it holds a character TEMPLATE id. The C#
    /// property rename (<c>CharacterTemplateId</c>) already forces every call site to be revisited, so the physical
    /// rename is deliberately a SEPARATE later step: doing DDL and a data re-key in one turn is the riskier
    /// combination, and the rename changes no behaviour. Recorded as B-127 P3 work.
    /// </summary>
    private const string DeferredRenameNote =
        "Deferred to B-127 P3: the physical column rename (CharacterProfileId -> CharacterTemplateId) on the identity "
        + "and character-scoped tables. The value space changed in this run; only the column NAME is still legacy.";

    public static async Task<int> RunAsync(
        SqliteConnection connection, string mode, IReadOnlyList<string> extraArguments)
    {
        if (string.Equals(mode, "preview", StringComparison.OrdinalIgnoreCase))
        {
            var report = await BuildReportAsync(connection);
            PrintReport(report, extraArguments);

            // A non-zero code means "the apply would refuse": visible without reading the whole report.
            return report.Unresolved.Count > 0 && !SkipsUnresolved(extraArguments) ? 3 : 0;
        }

        if (string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase))
        {
            return await ApplyAsync(connection, extraArguments);
        }

        throw new InvalidOperationException(
            $"'{mode}' is not a supported mode. Use 'preview' (read-only plan) or 'apply' (renames the identity key "
            + "columns and re-keys the rows, in one transaction).");
    }

    private static bool SkipsUnresolved(IReadOnlyList<string> arguments)
        => arguments.Any(argument => string.Equals(argument, "--skip-unresolved", StringComparison.OrdinalIgnoreCase));

    private static bool DemotesShadowedDrafts(IReadOnlyList<string> arguments)
        => arguments.Any(argument => string.Equals(argument, "--demote-shadowed-drafts", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>--link instanceId=templateId</c> pairs. The operator names BOTH ids; nothing is matched by name, and the
    /// template is checked to be a character template before it is written.
    /// </summary>
    private static Dictionary<string, string> ParseLinks(IReadOnlyList<string> arguments)
    {
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in arguments.Where(argument => argument.StartsWith("--link=", StringComparison.OrdinalIgnoreCase)))
        {
            var pair = argument["--link=".Length..];
            var separator = pair.IndexOf('=');
            if (separator <= 0 || separator == pair.Length - 1)
            {
                throw new InvalidOperationException(
                    $"'{argument}' is not a link. Use --link=<instance id>=<character template id>.");
            }

            links[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
        }

        return links;
    }

    private static async Task<int> ApplyAsync(SqliteConnection connection, IReadOnlyList<string> arguments)
    {
        Console.WriteLine("B-127 identity rekey - APPLY");
        Console.WriteLine();

        // 1. Explicit links first, so the resolution below sees them.
        var links = ParseLinks(arguments);
        foreach (var (instanceId, templateId) in links)
        {
            var templateName = await RequireCharacterTemplateAsync(connection, templateId);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO CharacterIdentityLinks (OwnerInstanceId, CharacterTemplateId, LinkedBy, LinkedUtc)
                VALUES ($instanceId, $templateId, $linkedBy, $linkedUtc)
                ON CONFLICT(OwnerInstanceId) DO UPDATE SET
                    CharacterTemplateId = excluded.CharacterTemplateId,
                    LinkedBy = excluded.LinkedBy,
                    LinkedUtc = excluded.LinkedUtc;
                """;
            command.Parameters.AddWithValue("$instanceId", instanceId);
            command.Parameters.AddWithValue("$templateId", templateId);
            command.Parameters.AddWithValue("$linkedBy", "b127-identity-rekey apply");
            command.Parameters.AddWithValue("$linkedUtc", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
            Console.WriteLine($"  linked {instanceId} -> {templateId} ({templateName})");
        }

        // 2. Re-read the plan with the links in place and hold every precondition before writing anything.
        var report = await BuildReportAsync(connection);
        if (report.Unresolved.Count > 0 && !SkipsUnresolved(arguments))
        {
            Console.Error.WriteLine(
                $"Refusing: {report.Unresolved.Count} owner(s) still cannot be resolved. Link them explicitly "
                + "(--link=<instance id>=<template id>), or pass --skip-unresolved to leave those rows keyed as they "
                + "are; a later apply run picks them up once they are linked.");
            foreach (var owner in report.Unresolved)
            {
                Console.Error.WriteLine($"  {owner.OwnerId} - {owner.Description}");
            }

            return 3;
        }

        var conflicts = report.Plans.Where(plan => plan.ApprovedCount > 1).ToList();
        if (conflicts.Count > 0)
        {
            Console.Error.WriteLine("Refusing: the merge would leave more than one approved pack per character:");
            foreach (var conflict in conflicts)
            {
                Console.Error.WriteLine($"  {conflict.TemplateId} {conflict.TemplateName}: {conflict.ApprovedCount} approved");
            }

            Console.Error.WriteLine("Decide which pack stays approved (supersede the others) and re-run.");
            return 3;
        }

        var shadowed = report.Plans.Where(plan => plan.ShadowedDrafts.Count > 0).ToList();
        if (shadowed.Count > 0 && !DemotesShadowedDrafts(arguments))
        {
            Console.Error.WriteLine("Refusing: a draft would end up below an approved pack, where approving it later could never make it the active pack:");
            foreach (var plan in shadowed)
            {
                foreach (var draft in plan.ShadowedDrafts)
                {
                    Console.Error.WriteLine($"  {plan.TemplateId} {plan.TemplateName}: {draft.PackId} lands at v{draft.NewVersion}");
                }
            }

            Console.Error.WriteLine("Pass --demote-shadowed-drafts to mark those drafts Superseded (their assets are kept), or resolve them yourself and re-run.");
            return 3;
        }

        // 3. One transaction, and Renumbering comes BEFORE the re-key. Two per-instance chains both start at v1, so
        // re-keying first would collide on UNIQUE (CharacterTemplateId, Version) the moment the second chain lands
        // under the template. Order: renumber each chain while its rows are still grouped under their own key, then
        // move the groups together.
        await using var transaction = await connection.BeginTransactionAsync();

        // Two phases per plan: shift every version far out of the target range first (a high POSITIVE offset — the
        // table's CHECK (Version > 0) forbids the negative trick), so no intermediate state can collide with a
        // version that is still to be written.
        var renumbered = 0;
        foreach (var plan in report.Plans)
        {
            foreach (var pack in plan.Packs)
            {
                renumbered += await ExecuteAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    $"UPDATE CharacterImageIdentityPacks SET Version = Version + {VersionOffset} WHERE Id = $packId;",
                    ("$packId", pack.PackId));
            }

            foreach (var pack in plan.Packs)
            {
                renumbered += await ExecuteAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    "UPDATE CharacterImageIdentityPacks SET Version = $version WHERE Id = $packId;",
                    ("$version", pack.NewVersion.ToString()),
                    ("$packId", pack.PackId));
            }
        }

        var rekeyed = 0;
        foreach (var owner in report.Owners.Where(owner => owner.TemplateId is not null))
        {
            foreach (var table in IdentityTables)
            {
                rekeyed += await ExecuteAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    $"UPDATE {table} SET {OwnerColumn} = $templateId WHERE {OwnerColumn} = $ownerId;",
                    ("$templateId", owner.TemplateId!),
                    ("$ownerId", owner.OwnerId));
            }

            foreach (var table in CharacterScopedTables)
            {
                rekeyed += await ExecuteAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    $"UPDATE {table} SET CharacterProfileId = $templateId WHERE CharacterProfileId = $ownerId;",
                    ("$templateId", owner.TemplateId!),
                    ("$ownerId", owner.OwnerId));
            }
        }

        var demoted = 0;
        if (DemotesShadowedDrafts(arguments))
        {
            foreach (var draft in shadowed.SelectMany(plan => plan.ShadowedDrafts))
            {
                demoted += await ExecuteAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    "UPDATE CharacterImageIdentityPacks SET Status = 'Superseded' WHERE Id = $packId AND Status = 'Draft';",
                    ("$packId", draft.PackId));
            }
        }

        await transaction.CommitAsync();

        Console.WriteLine();
        Console.WriteLine($"Applied: {rekeyed} row(s) re-keyed, {renumbered} version set(s), {demoted} shadowed draft(s) demoted.");
        Console.WriteLine($"  {DeferredRenameNote}");
        if (report.Unresolved.Count > 0)
        {
            Console.WriteLine("Left keyed as they are (still unlinked — nothing was guessed):");
            foreach (var owner in report.Unresolved)
            {
                Console.WriteLine($"  {owner.OwnerId} - {owner.Description} (builds: {owner.Builds})");
            }

            Console.WriteLine("Link them and run apply again: the command is idempotent and re-runnable.");
        }

        Console.WriteLine();
        Console.WriteLine("Re-run 'preview' to see the result.");
        return 0;
    }

    private static async Task<string> RequireCharacterTemplateAsync(SqliteConnection connection, string templateId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Templates WHERE Id = $id AND TemplateType = 'Character';";
        command.Parameters.AddWithValue("$id", templateId);
        var name = await command.ExecuteScalarAsync();
        return name as string
            ?? throw new InvalidOperationException(
                $"'{templateId}' is not a character template, so it cannot own character identity.");
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, string Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<Report> BuildReportAsync(SqliteConnection connection)
    {
        var templates = await ReadTemplatesAsync(connection);
        var scenarioCharacters = await ReadScenarioCharactersAsync(connection);
        var links = await ReadLinksAsync(connection);
        var assets = await ReadCharacterAssetsAsync(connection);
        var ownerColumn = await ResolveOwnerColumnAsync(connection, IdentityTables[0]);

        var owners = new List<OwnerRow>();
        foreach (var ownerId in await ReadOwnerIdsAsync(connection, ownerColumn))
        {
            var packs = await ReadPacksAsync(connection, ownerColumn, ownerId);
            var templateId = Resolve(ownerId, templates, scenarioCharacters, links, assets);
            owners.Add(new OwnerRow(
                ownerId,
                Describe(ownerId, templates, scenarioCharacters, links, assets),
                templateId,
                templateId is null
                    ? null
                    : templates.FirstOrDefault(template =>
                        string.Equals(template.Id, templateId, StringComparison.OrdinalIgnoreCase)).Name,
                packs,
                await CountAsync(connection, "CharacterIdentityBuilds", ownerColumn, ownerId),
                await CountAsync(connection, "CharacterBodyCards", ownerColumn, ownerId),
                await CountAsync(connection, "CharacterLoraDatasets", ownerColumn, ownerId)));
        }

        var plans = BuildPlans(owners);
        return new Report(
            templates,
            owners,
            plans,
            owners.Where(owner => owner.TemplateId is null).ToList(),
            ownerColumn,
            TemplateColumnRenamed: !string.Equals(ownerColumn, "CharacterProfileId", StringComparison.Ordinal));
    }

    /// <summary>The legacy physical column name the value re-key writes through (see <see cref="DeferredRenameNote"/>).</summary>
    private const string OwnerColumn = "CharacterProfileId";

    /// <summary>
    /// The temporary version offset used while renumbering. High enough that no shifted value can collide with a
    /// final one, and positive because the table's CHECK constraint requires Version > 0.
    /// </summary>
    private const int VersionOffset = 100000;

    /// <summary>Re-key + renumber plan per template: packs ordered by <c>CreatedUtc</c>, then by id for stability.</summary>
    private static List<TemplatePlan> BuildPlans(IReadOnlyList<OwnerRow> owners)
    {
        var plans = new List<TemplatePlan>();
        foreach (var group in owners
                     .Where(owner => owner.TemplateId is not null)
                     .GroupBy(owner => owner.TemplateId!, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .SelectMany(owner => owner.Packs.Select(pack => (Owner: owner, Pack: pack)))
                .OrderBy(item => item.Pack.CreatedUtc, StringComparer.Ordinal)
                .ThenBy(item => item.Pack.Id, StringComparer.Ordinal)
                .ToList();

            var renumbering = new List<PackRenumbering>();
            for (var index = 0; index < ordered.Count; index++)
            {
                var item = ordered[index];
                renumbering.Add(new PackRenumbering(
                    item.Owner.OwnerId,
                    item.Pack.Id,
                    item.Pack.Version,
                    index + 1,
                    item.Pack.Status,
                    item.Pack.SupersedesId));
            }

            plans.Add(new TemplatePlan(
                group.Key,
                group.First().TemplateName ?? string.Empty,
                group.Select(owner => owner.OwnerId).ToList(),
                renumbering,
                renumbering.Count(pack => string.Equals(pack.Status, "Approved", StringComparison.OrdinalIgnoreCase)),
                HighestApprovedVersion(renumbering),
                ShadowedDrafts(renumbering)));
        }

        return plans;
    }

    private static int HighestApprovedVersion(IReadOnlyList<PackRenumbering> packs)
        => packs
            .Where(pack => string.Equals(pack.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            .Select(pack => pack.NewVersion)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// Drafts that would land BELOW an approved pack. They matter: <c>GetLatestApprovedPackAsync</c> returns the
    /// highest-versioned approved pack, so approving such a draft later would not become the character's active
    /// pack — the merge has to resolve the shape explicitly instead of leaving a trap.
    /// </summary>
    private static List<PackRenumbering> ShadowedDrafts(IReadOnlyList<PackRenumbering> packs)
    {
        var highestApproved = HighestApprovedVersion(packs);
        return packs
            .Where(pack => pack.NewVersion < highestApproved
                           && string.Equals(pack.Status, "Draft", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static void PrintReport(Report report, IReadOnlyList<string> extraArguments)
    {
        Console.WriteLine("B-127 identity rekey - PREVIEW (read-only; nothing was written)");
        Console.WriteLine();
        Console.WriteLine($"Identity key column: {report.OwnerColumn} (legacy name; the VALUE is the character template after this re-key)");
        Console.WriteLine();

        Console.WriteLine($"Character templates ({report.Templates.Count}):");
        foreach (var template in report.Templates)
        {
            Console.WriteLine($"  {template.Id}  {template.Name}");
        }

        Console.WriteLine();
        Console.WriteLine($"Identity owners found ({report.Owners.Count}):");
        foreach (var owner in report.Owners)
        {
            var target = owner.TemplateId is null
                ? "UNRESOLVED"
                : $"{owner.TemplateId} {owner.TemplateName}";
            Console.WriteLine($"  {owner.OwnerId}");
            Console.WriteLine($"    is: {owner.Description}");
            Console.WriteLine($"    -> template: {target}");
            Console.WriteLine($"    packs: {owner.Packs.Count}{(owner.Packs.Count == 0 ? string.Empty : " (" + string.Join(", ", owner.Packs.Select(pack => $"v{pack.Version} {pack.Status}")) + ")")}"
                              + $"  builds: {owner.Builds}  body cards: {owner.BodyCards}  lora datasets: {owner.LoraDatasets}");
            if (owner.TemplateId is null)
            {
                Console.WriteLine("    remedy: link it explicitly before applying, e.g.");
                Console.WriteLine($"            --link {owner.OwnerId}=<character template id from the list above>");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Merge plan per character template:");
        if (report.Plans.Count == 0)
        {
            Console.WriteLine("  (no packs to re-key)");
        }

        foreach (var plan in report.Plans)
        {
            Console.WriteLine($"  {plan.TemplateId} {plan.TemplateName}: {plan.Packs.Count} pack(s) from {plan.Owners.Count} instance(s)");
            foreach (var pack in plan.Packs.Where(pack => pack.OldVersion != pack.NewVersion))
            {
                Console.WriteLine($"      {pack.OwnerId}  {pack.PackId[..Math.Min(8, pack.PackId.Length)]}..  v{pack.OldVersion} -> v{pack.NewVersion}  ({pack.Status})");
            }

            Console.WriteLine($"      approved packs after the merge: {plan.ApprovedCount}"
                              + (plan.ApprovedCount > 1 ? "   <-- CONFLICT: the apply will refuse until one is superseded" : string.Empty));
            if (plan.ShadowedDrafts.Count > 0)
            {
                Console.WriteLine($"      HIGHEST approved pack after the merge: v{(plan.HighestApprovedVersion == 0 ? "none" : plan.HighestApprovedVersion.ToString())}");
                Console.WriteLine("      SHADOWED DRAFT: a draft lands below an approved pack:");
                foreach (var draft in plan.ShadowedDrafts)
                {
                    Console.WriteLine($"        {draft.OwnerId}  {draft.PackId[..Math.Min(8, draft.PackId.Length)]}..  v{draft.NewVersion}");
                }

                Console.WriteLine("        Approving such a draft later would NOT become the character's active pack (the reader");
                Console.WriteLine("        returns the highest-versioned approved pack). The apply refuses this shape unless the");
                Console.WriteLine("        operator resolves it explicitly: --demote-shadowed-drafts (mark them Superseded).");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Character-scoped rows that move with the same owner (prompt overrides, per-character settings):");
        foreach (var table in CharacterScopedTables)
        {
            Console.WriteLine($"  {table}.CharacterProfileId -> the owner's character template");
        }

        Console.WriteLine();
        Console.WriteLine("Physical column rename: NOT performed here (B-127 P3). The VALUE space becomes the character template;");
        Console.WriteLine("the legacy column NAME is renamed in a separate step:");
        foreach (var table in IdentityTables)
        {
            Console.WriteLine($"  {table}.{report.OwnerColumn} (holds the character template id after this run; name unchanged)");
        }

        Console.WriteLine("  SceneAssets.CharacterProfileId: unchanged - it records the origin instance, not ownership");
        Console.WriteLine();
        if (report.Unresolved.Count > 0)
        {
            Console.WriteLine($"BLOCKED: {report.Unresolved.Count} owner(s) cannot be resolved, so apply would refuse:");
            foreach (var owner in report.Unresolved)
            {
                Console.WriteLine($"  {owner.OwnerId} - {owner.Description}");
            }

            Console.WriteLine("Link each one explicitly (the running app's Link character identity action, or");
            Console.WriteLine("--link=<instance id>=<template id> on the apply), then run the preview again.");
            Console.WriteLine("--skip-unresolved lets the apply run anyway, leaving those rows keyed as they are.");
        }
        else
        {
            Console.WriteLine("No unresolved owners: the apply can run.");
        }

        if (extraArguments.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Note: link arguments are accepted by the apply step; the preview reports the plan as stored.");
        }
    }

    // ---------------- reads ----------------

    private static async Task<List<(string Id, string Name)>> ReadTemplatesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM Templates WHERE TemplateType = 'Character' ORDER BY Name;";
        var templates = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            templates.Add((reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        }

        return templates;
    }

    private static async Task<List<ScenarioCharacter>> ReadScenarioCharactersAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.Name,
                   json_extract(c.value, '$.Id'),
                   json_extract(c.value, '$.Name'),
                   json_extract(c.value, '$.TemplateId')
            FROM Scenarios s, json_each(s.PayloadJson, '$.Characters') c;
            """;
        var characters = new List<ScenarioCharacter>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            characters.Add(new ScenarioCharacter(
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return characters;
    }

    private static async Task<Dictionary<string, string>> ReadLinksAsync(SqliteConnection connection)
    {
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!await TableExistsAsync(connection, "CharacterIdentityLinks"))
        {
            return links;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT OwnerInstanceId, CharacterTemplateId FROM CharacterIdentityLinks;";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            links[reader.GetString(0)] = reader.GetString(1);
        }

        return links;
    }

    private static async Task<HashSet<string>> ReadCharacterAssetsAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM SceneAssets WHERE Type = 'Character';";
        var assets = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            assets.Add(reader.GetString(0));
        }

        return assets;
    }

    private static async Task<List<string>> ReadOwnerIdsAsync(SqliteConnection connection, string ownerColumn)
    {
        var ids = new List<string>();
        foreach (var table in IdentityTables)
        {
            if (!await TableExistsAsync(connection, table))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT DISTINCT {ownerColumn} FROM {table};";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                if (!ids.Contains(id, StringComparer.Ordinal))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private static async Task<List<Pack>> ReadPacksAsync(
        SqliteConnection connection, string ownerColumn, string ownerId)
    {
        if (!await TableExistsAsync(connection, "CharacterImageIdentityPacks"))
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, Version, Status, SupersedesId, CreatedUtc
            FROM CharacterImageIdentityPacks
            WHERE {ownerColumn} = $ownerId
            ORDER BY Version;
            """;
        command.Parameters.AddWithValue("$ownerId", ownerId);
        var packs = new List<Pack>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            packs.Add(new Pack(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        return packs;
    }

    private static async Task<int> CountAsync(
        SqliteConnection connection, string table, string ownerColumn, string ownerId)
    {
        if (!await TableExistsAsync(connection, table))
        {
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {ownerColumn} = $ownerId;";
        command.Parameters.AddWithValue("$ownerId", ownerId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    /// <summary>The identity key column: already renamed after the apply, so the command stays runnable and honest.</summary>
    private static async Task<string> ResolveOwnerColumnAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        if (columns.Contains("CharacterTemplateId", StringComparer.Ordinal))
        {
            return "CharacterTemplateId";
        }

        if (columns.Contains("CharacterProfileId", StringComparer.Ordinal))
        {
            return "CharacterProfileId";
        }

        throw new InvalidOperationException(
            $"Table '{table}' has neither CharacterTemplateId nor CharacterProfileId, so the identity key cannot be "
            + "identified. Check the database before running this command.");
    }

    // ---------------- resolution (the app's rule set, applied to raw rows) ----------------

    private static string? Resolve(
        string ownerId,
        IReadOnlyList<(string Id, string Name)> templates,
        IReadOnlyList<ScenarioCharacter> scenarioCharacters,
        IReadOnlyDictionary<string, string> links,
        IReadOnlySet<string> assets)
    {
        if (templates.Any(template => string.Equals(template.Id, ownerId, StringComparison.OrdinalIgnoreCase)))
        {
            return ownerId;
        }

        var scenarioCharacter = scenarioCharacters.FirstOrDefault(
            character => string.Equals(character.Id, ownerId, StringComparison.Ordinal));
        if (scenarioCharacter is not null && !string.IsNullOrWhiteSpace(scenarioCharacter.TemplateId))
        {
            return scenarioCharacter.TemplateId;
        }

        if (links.TryGetValue(ownerId, out var linked))
        {
            return linked;
        }

        return null;
    }

    private static string Describe(
        string ownerId,
        IReadOnlyList<(string Id, string Name)> templates,
        IReadOnlyList<ScenarioCharacter> scenarioCharacters,
        IReadOnlyDictionary<string, string> links,
        IReadOnlySet<string> assets)
    {
        var template = templates.FirstOrDefault(t => string.Equals(t.Id, ownerId, StringComparison.OrdinalIgnoreCase));
        if (template.Id is not null)
        {
            return $"character template '{template.Name}'";
        }

        var scenarioCharacter = scenarioCharacters.FirstOrDefault(c => string.Equals(c.Id, ownerId, StringComparison.Ordinal));
        if (scenarioCharacter is not null)
        {
            var templateNote = string.IsNullOrWhiteSpace(scenarioCharacter.TemplateId)
                ? "carrying no TemplateId"
                : $"TemplateId {scenarioCharacter.TemplateId}";
            return $"scenario character '{scenarioCharacter.Name}' in '{scenarioCharacter.ScenarioName}' ({templateNote})";
        }

        if (assets.Contains(ownerId))
        {
            return "character asset (SceneAssets, Type='Character')"
                   + (links.ContainsKey(ownerId) ? $", linked to {links[ownerId]}" : ", no link row");
        }

        return "unknown to every namespace";
    }

    private sealed record ScenarioCharacter(string Id, string Name, string ScenarioName, string? TemplateId);

    private sealed record Pack(string Id, int Version, string Status, string? SupersedesId, string CreatedUtc);

    private sealed record OwnerRow(
        string OwnerId,
        string Description,
        string? TemplateId,
        string? TemplateName,
        List<Pack> Packs,
        int Builds,
        int BodyCards,
        int LoraDatasets);

    private sealed record PackRenumbering(
        string OwnerId, string PackId, int OldVersion, int NewVersion, string Status, string? SupersedesId);

    private sealed record TemplatePlan(
        string TemplateId,
        string TemplateName,
        List<string> Owners,
        List<PackRenumbering> Packs,
        int ApprovedCount,
        int HighestApprovedVersion,
        List<PackRenumbering> ShadowedDrafts);

    private sealed record Report(
        List<(string Id, string Name)> Templates,
        List<OwnerRow> Owners,
        List<TemplatePlan> Plans,
        List<OwnerRow> Unresolved,
        string OwnerColumn,
        bool TemplateColumnRenamed);
}
