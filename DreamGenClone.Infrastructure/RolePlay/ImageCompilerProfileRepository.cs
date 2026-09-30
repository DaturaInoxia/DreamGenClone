using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

/// <summary>
/// SQLite persistence for the per-checkpoint image compiler profiles (B-135 B135-001/002).
///
/// <para>
/// Schema creation and seeding are idempotent, and the seed is reached from the SAME private ensure that
/// <c>OpenAsync</c> calls, so any read seeds — a store whose data must exist for the store to be usable cannot rely
/// on a startup path someone forgets to call.
/// </para>
///
/// <para>
/// Seeding never overwrites an existing row for a checkpoint: a profile is editable configuration, and a hand-edited
/// row must survive the next open.
/// </para>
/// </summary>
public sealed class ImageCompilerProfileRepository : IImageCompilerProfileRepository
{
    private const string SelectColumns = """
        Id, CheckpointIdentifier, DisplayName, Family, PromptDialect, MinChars, MaxChars, MaxTokens,
        RequiredComponentsJson, ForbiddenTokensJson, PoseInText, Negative, NegativeSource,
        SettingsEnvelopeJson, SystemPrompt, ExamplesJson, ResearchSource, Version, UpdatedUtc
        """;

    private readonly string _connectionString;

    public ImageCompilerProfileRepository(IOptions<PersistenceOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
    }

    public async Task<ImageCompilerProfile?> FindByCheckpointAsync(string checkpointIdentifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(checkpointIdentifier))
        {
            throw new InvalidOperationException("A checkpoint identifier is required to resolve an image compiler profile.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM ImageCompilerProfiles
            WHERE CheckpointIdentifier = $checkpoint COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$checkpoint", checkpointIdentifier.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProfile(reader) : null;
    }

    public async Task<IReadOnlyList<ImageCompilerProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM ImageCompilerProfiles
            ORDER BY Family, DisplayName;
            """;
        var results = new List<ImageCompilerProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadProfile(reader));
        }

        return results;
    }

    public async Task UpsertAsync(ImageCompilerProfile profile, CancellationToken cancellationToken = default)
    {
        ImageCompilerProfileValidation.Validate(profile);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ImageCompilerProfiles (
                Id, CheckpointIdentifier, DisplayName, Family, PromptDialect, MinChars, MaxChars, MaxTokens,
                RequiredComponentsJson, ForbiddenTokensJson, PoseInText, Negative, NegativeSource,
                SettingsEnvelopeJson, SystemPrompt, ExamplesJson, ResearchSource, Version, UpdatedUtc)
            VALUES (
                $id, $checkpoint, $displayName, $family, $dialect, $minChars, $maxChars, $maxTokens,
                $required, $forbidden, $poseInText, $negative, $negativeSource,
                $envelope, $systemPrompt, $examples, $researchSource, $version, $updatedUtc)
            ON CONFLICT(CheckpointIdentifier) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                Family = excluded.Family,
                PromptDialect = excluded.PromptDialect,
                MinChars = excluded.MinChars,
                MaxChars = excluded.MaxChars,
                MaxTokens = excluded.MaxTokens,
                RequiredComponentsJson = excluded.RequiredComponentsJson,
                ForbiddenTokensJson = excluded.ForbiddenTokensJson,
                PoseInText = excluded.PoseInText,
                Negative = excluded.Negative,
                NegativeSource = excluded.NegativeSource,
                SettingsEnvelopeJson = excluded.SettingsEnvelopeJson,
                SystemPrompt = excluded.SystemPrompt,
                ExamplesJson = excluded.ExamplesJson,
                ResearchSource = excluded.ResearchSource,
                Version = excluded.Version,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        AddProfileParameters(command, profile);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddProfileParameters(SqliteCommand command, ImageCompilerProfile profile)
    {
        command.Parameters.AddWithValue("$id", profile.Id.Trim());
        command.Parameters.AddWithValue("$checkpoint", profile.CheckpointIdentifier.Trim());
        command.Parameters.AddWithValue("$displayName", profile.DisplayName.Trim());
        command.Parameters.AddWithValue("$family", (int)profile.Family);
        command.Parameters.AddWithValue("$dialect", (int)profile.PromptDialect);
        command.Parameters.AddWithValue("$minChars", profile.MinChars);
        command.Parameters.AddWithValue("$maxChars", profile.MaxChars);
        command.Parameters.AddWithValue("$maxTokens", profile.MaxTokens);
        command.Parameters.AddWithValue("$required", profile.RequiredComponentsJson);
        command.Parameters.AddWithValue("$forbidden", profile.ForbiddenTokensJson);
        command.Parameters.AddWithValue("$poseInText", (int)profile.PoseInText);
        command.Parameters.AddWithValue("$negative", profile.Negative);
        command.Parameters.AddWithValue("$negativeSource", (object?)profile.NegativeSource ?? DBNull.Value);
        command.Parameters.AddWithValue("$envelope", profile.SettingsEnvelopeJson);
        command.Parameters.AddWithValue("$systemPrompt", profile.SystemPrompt);
        command.Parameters.AddWithValue("$examples", profile.ExamplesJson);
        command.Parameters.AddWithValue("$researchSource", profile.ResearchSource);
        command.Parameters.AddWithValue("$version", profile.Version);
        command.Parameters.AddWithValue("$updatedUtc", profile.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static ImageCompilerProfile ReadProfile(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("Id")),
        CheckpointIdentifier = reader.GetString(reader.GetOrdinal("CheckpointIdentifier")),
        DisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
        Family = (SceneImageModelFamily)reader.GetInt32(reader.GetOrdinal("Family")),
        PromptDialect = (SceneImagePromptDialect)reader.GetInt32(reader.GetOrdinal("PromptDialect")),
        MinChars = reader.GetInt32(reader.GetOrdinal("MinChars")),
        MaxChars = reader.GetInt32(reader.GetOrdinal("MaxChars")),
        MaxTokens = reader.GetInt32(reader.GetOrdinal("MaxTokens")),
        RequiredComponentsJson = reader.GetString(reader.GetOrdinal("RequiredComponentsJson")),
        ForbiddenTokensJson = reader.GetString(reader.GetOrdinal("ForbiddenTokensJson")),
        PoseInText = (ImagePoseInText)reader.GetInt32(reader.GetOrdinal("PoseInText")),
        Negative = reader.GetString(reader.GetOrdinal("Negative")),
        NegativeSource = reader.IsDBNull(reader.GetOrdinal("NegativeSource")) ? null : reader.GetString(reader.GetOrdinal("NegativeSource")),
        SettingsEnvelopeJson = reader.GetString(reader.GetOrdinal("SettingsEnvelopeJson")),
        SystemPrompt = reader.GetString(reader.GetOrdinal("SystemPrompt")),
        ExamplesJson = reader.GetString(reader.GetOrdinal("ExamplesJson")),
        ResearchSource = reader.GetString(reader.GetOrdinal("ResearchSource")),
        Version = reader.GetInt32(reader.GetOrdinal("Version")),
        UpdatedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedUtc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ImageCompilerProfiles (
                    Id TEXT PRIMARY KEY,
                    CheckpointIdentifier TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    Family INTEGER NOT NULL,
                    PromptDialect INTEGER NOT NULL,
                    MinChars INTEGER NOT NULL,
                    MaxChars INTEGER NOT NULL,
                    MaxTokens INTEGER NOT NULL,
                    RequiredComponentsJson TEXT NOT NULL DEFAULT '[]',
                    ForbiddenTokensJson TEXT NOT NULL DEFAULT '[]',
                    PoseInText INTEGER NOT NULL,
                    Negative TEXT NOT NULL DEFAULT '',
                    NegativeSource TEXT NULL,
                    SettingsEnvelopeJson TEXT NOT NULL DEFAULT '{}',
                    SystemPrompt TEXT NOT NULL DEFAULT '',
                    ExamplesJson TEXT NOT NULL DEFAULT '[]',
                    ResearchSource TEXT NOT NULL DEFAULT '',
                    Version INTEGER NOT NULL DEFAULT 1,
                    UpdatedUtc TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ImageCompilerProfiles_Checkpoint
                    ON ImageCompilerProfiles (CheckpointIdentifier COLLATE NOCASE);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await SeedAsync(connection, cancellationToken);
    }

    private static async Task SeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var profile in SeedProfiles())
        {
            // A bad seed row is a bug in this file, not user data: refuse it loudly rather than writing a
            // profile that would compile prompts against a rule nobody can satisfy.
            ImageCompilerProfileValidation.Validate(profile);

            await using var command = connection.CreateCommand();
            // DO NOTHING, not DO UPDATE: an existing row may have been edited, and a seed must never revert
            // configuration the operator changed.
            command.CommandText = """
                INSERT INTO ImageCompilerProfiles (
                    Id, CheckpointIdentifier, DisplayName, Family, PromptDialect, MinChars, MaxChars, MaxTokens,
                    RequiredComponentsJson, ForbiddenTokensJson, PoseInText, Negative, NegativeSource,
                    SettingsEnvelopeJson, SystemPrompt, ExamplesJson, ResearchSource, Version, UpdatedUtc)
                VALUES (
                    $id, $checkpoint, $displayName, $family, $dialect, $minChars, $maxChars, $maxTokens,
                    $required, $forbidden, $poseInText, $negative, $negativeSource,
                    $envelope, $systemPrompt, $examples, $researchSource, $version, $updatedUtc)
                ON CONFLICT(CheckpointIdentifier) DO NOTHING;
                """;
            AddProfileParameters(command, profile);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // The migration rows. Checkpoint identifiers are the REAL ModelIdentifier values from RegisteredModels
    // (verified against the dev DB 2026-09-29), never invented. Where the canonical research doc gives a value it
    // is used and cited; where it does not, the row says so in ResearchSource and the value is marked provisional
    // so it cannot be mistaken for researched guidance.
    // ---------------------------------------------------------------------------------------------------------
    private static IReadOnlyList<ImageCompilerProfile> SeedProfiles() =>
    [
        new()
        {
            Id = "profile-biglust-v16",
            CheckpointIdentifier = "bigLust_v16.safetensors",
            DisplayName = "BigLust v1.6",
            Family = SceneImageModelFamily.Sdxl,
            PromptDialect = SceneImagePromptDialect.SdxlNaturalLanguage,
            MinChars = 120,
            MaxChars = 600,
            MaxTokens = 75,
            RequiredComponentsJson = """["subject","action","framing","lighting","clothing-when-clothed"]""",
            ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame","pony-tag"]""",
            // B-135 D18: pose text does not work on this checkpoint, especially with more than one person.
            PoseInText = ImagePoseInText.Forbidden,
            Negative = string.Empty,
            NegativeSource = null,
            SettingsEnvelopeJson = """{"sampler":"dpmpp_2m_sde","scheduler":"karras","steps":30,"cfg":5.0,"resolution":"1024x1024"}""",
            ResearchSource =
                "scene-image-prompt-compiler-standards.instructions.md §3.1 (SDXL envelope; ~75 tokens / 600-800 chars; "
                + "production dpmpp_2m_sde/karras/30/CFG 5.0/1024x1024; BigLust community CFG 4-5). "
                + "BigLust has NO author-written prompt guide, so the prompt SHAPE is operator-stated and is to be "
                + "derived from measured A/B in the Playground (B135-037). Negative empty: BigLust v1.6 examples use an empty negative.",
        },
        new()
        {
            Id = "profile-juggernaut-ragnarok",
            CheckpointIdentifier = "juggernautXL_ragnarok.safetensors",
            DisplayName = "Juggernaut XL Ragnarok",
            Family = SceneImageModelFamily.Sdxl,
            PromptDialect = SceneImagePromptDialect.SdxlNaturalLanguage,
            MinChars = 120,
            MaxChars = 600,
            MaxTokens = 75,
            RequiredComponentsJson = """["subject","action","framing","lighting","clothing-when-clothed"]""",
            ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame","pony-tag"]""",
            PoseInText = ImagePoseInText.Forbidden,
            Negative = string.Empty,
            NegativeSource = null,
            SettingsEnvelopeJson = """{"sampler":"dpmpp_2m_sde","scheduler":"karras","steps":30,"cfg":5.0,"resolution":"832x1216"}""",
            ResearchSource =
                "scene-image-prompt-compiler-standards.instructions.md §3.1 + the official Juggernaut XIII Ragnarok prompt guide "
                + "(832x1216 portrait; DPM++ 2M SDE; 30-40 steps; CFG 3-6). Negative empty: the author's negatives are "
                + "SFW-avoidance practice and do not apply to this app's adult renders.",
        },
        new()
        {
            Id = "profile-pony-realism-v23-ultra",
            CheckpointIdentifier = "ponyRealism_V23ULTRA.safetensors",
            DisplayName = "Pony Realism v2.3 ULTRA",
            Family = SceneImageModelFamily.Pony,
            PromptDialect = SceneImagePromptDialect.PonyV6Tags,
            MinChars = 80,
            MaxChars = 600,
            MaxTokens = 75,
            RequiredComponentsJson = """["quality-tag-string","rating-tag","subject-count-tag","camera-view"]""",
            ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame","natural-language-prose"]""",
            PoseInText = ImagePoseInText.SimpleOnly,
            // The ONE cited negative in the system (B-135 D10). Pony ignores "no X" in the positive, so a negation
            // placed anywhere else simply does not happen.
            Negative = "lowres, bad anatomy, bad hands, extra digits, watermark, text, blurry",
            NegativeSource =
                "pony-v6-prompting.instructions.md rules 8-9 (Pony is designed not to need negative prompts in most cases; "
                + "a huge negative fights the model; Pony IGNORES 'no X' in the positive so negations must live in the negative) "
                + "and the Pony V6 author's score_9 explainer (score drops in the negative are weak). Score drops deliberately excluded.",
            SettingsEnvelopeJson = """{"sampler":"euler_a","steps":30,"cfg":6.5,"resolution":"1216x1216","clipSkip":2}""",
            ResearchSource =
                "scene-image-prompt-compiler-standards.instructions.md §3.4 (Pony Realism v2.3 ULTRA Civitai card, model 372465, "
                + "version 1920896: Euler A or DPM2 A, >=30 steps, CFG 6-7, >1024px, CLIP skip 2, Danbooru tags).",
        },
        new()
        {
            Id = "profile-pony-v6-xl",
            CheckpointIdentifier = "ponyDiffusionV6XL_v6.safetensors",
            DisplayName = "Pony V6 XL",
            Family = SceneImageModelFamily.Pony,
            PromptDialect = SceneImagePromptDialect.PonyV6Tags,
            MinChars = 80,
            MaxChars = 600,
            MaxTokens = 75,
            RequiredComponentsJson = """["quality-tag-string","rating-tag","subject-count-tag","camera-view"]""",
            ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame","natural-language-prose"]""",
            PoseInText = ImagePoseInText.SimpleOnly,
            Negative = "lowres, bad anatomy, bad hands, extra digits, watermark, text, blurry",
            NegativeSource =
                "pony-v6-prompting.instructions.md rules 8-9 (same citation as Pony Realism; Pony Realism is V6-derived and "
                + "the V6 author guidance applies unchanged).",
            SettingsEnvelopeJson = """{"sampler":"euler_ancestral","steps":25,"cfg":7.0,"resolution":"1024x1024","clipSkip":2}""",
            ResearchSource =
                "scene-image-prompt-compiler-standards.instructions.md §4 (Pony V6: euler_ancestral / 25 / 7, CLIP skip 2) + "
                + "pony-v6-prompting.instructions.md (full 6-tag quality string; explicit count tags; explicit camera view).",
        },
        new()
        {
            Id = "profile-flux1-dev-fp8",
            CheckpointIdentifier = "flux1-dev-fp8.safetensors",
            DisplayName = "FLUX.1-dev fp8 (local ComfyUI)",
            Family = SceneImageModelFamily.Flux,
            PromptDialect = SceneImagePromptDialect.FluxNaturalLanguage,
            MinChars = 150,
            MaxChars = 1200,
            MaxTokens = 256,
            RequiredComponentsJson = """["subject","action","setting","framing","lighting"]""",
            ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame"]""",
            // Measured (B-116): a dictated two-person pose is FRAGILE in text-T2I on this checkpoint; geometry holds
            // only via an img2img re-skin over a pose base (B-119 route C3).
            PoseInText = ImagePoseInText.SimpleOnly,
            Negative = string.Empty,
            NegativeSource = null,
            SettingsEnvelopeJson = """{"guidance":3.5,"cfg":1.0,"sampler":"euler","scheduler":"simple","resolution":"1024x1024"}""",
            ResearchSource =
                "B-112 (FluxGuidance 3.5 + cfg 1.0, empty negative — BFL: most FLUX models do not support negatives) + "
                + "B-116 measured finding (dictated two-person pose fragile in text; use the re-skin route). "
                + "MaxTokens 256 is PROVISIONAL (T5 window is larger); tighten in B135-037.",
        },
        new()
        {
            Id = "profile-qwen-image-2-1",
            CheckpointIdentifier = "qwen_image_2.1_int8_convrot.safetensors",
            DisplayName = "Qwen-Image-2.1 (Local ComfyUI)",
            Family = SceneImageModelFamily.QwenImage21,
            PromptDialect = SceneImagePromptDialect.NaturalLanguage,
            // Qwen-2.1 wants LONG, detailed prompts; this budget is the opposite of the SDXL-family envelope.
            MinChars = 300,
            MaxChars = 1600,
            MaxTokens = 300,
            RequiredComponentsJson = """["subject","action","setting","framing","lighting"]""",
            ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame","pony-tag"]""",
            PoseInText = ImagePoseInText.Full,
            Negative = string.Empty,
            NegativeSource = null,
            SettingsEnvelopeJson = """{"cfg":1.0,"sampler":"euler","scheduler":"simple"}""",
            ResearchSource =
                "QwenImage21SceneImagePromptCompiler (repo-verified: the official path runs cfg 1 with euler/simple, where the "
                + "negative is inert) + operator statement 2026-09-29 that Qwen needs large detailed prompts. "
                + "Character budget is PROVISIONAL; steps/resolution deliberately omitted rather than guessed. Tighten in B135-037.",
        },
        new()
        {
            Id = "profile-flux2-pro",
            CheckpointIdentifier = "black-forest-labs/FLUX.2-pro",
            DisplayName = "FLUX.2-pro",
            Family = SceneImageModelFamily.Api,
            PromptDialect = SceneImagePromptDialect.NaturalLanguage,
            MinChars = 200,
            MaxChars = 2000,
            MaxTokens = 400,
            RequiredComponentsJson = """["ordered-subjects","camera"]""",
            ForbiddenTokensJson = """["story-name","relationship","ownership","negative-prompt-field","pov-character-in-frame"]""",
            PoseInText = ImagePoseInText.Full,
            Negative = string.Empty,
            NegativeSource = null,
            SettingsEnvelopeJson = """{"dimensionsDivisibleBy":16,"maxMegapixels":4,"maxSteps":50}""",
            ResearchSource =
                "scene-image-prompt-compiler-standards.instructions.md §4.1 + flux2-prompting.instructions.md (FLUX.2 has no "
                + "negative prompt and the compiler rejects that field at any nesting level; dimensions divisible by 16; "
                + "<=4MP output; [flex] at most 50 steps).",
        },
        new() { Id = "profile-seedream-4", CheckpointIdentifier = "ByteDance-Seed/Seedream-4.0", DisplayName = "Seedream 4.0", Family = SceneImageModelFamily.Api, PromptDialect = SceneImagePromptDialect.NaturalLanguage, MinChars = 100, MaxChars = 1000, MaxTokens = 200, RequiredComponentsJson = """["subject","action","framing","lighting","clothing-when-clothed"]""", ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame"]""", PoseInText = ImagePoseInText.Full, Negative = string.Empty, SettingsEnvelopeJson = "{}", ResearchSource = "No published prompting guide captured yet; shape follows the API natural-language family row. PROVISIONAL — B135-037." },
        new() { Id = "profile-flux-1-1-pro", CheckpointIdentifier = "black-forest-labs/FLUX.1.1-pro", DisplayName = "FLUX.1.1 Pro", Family = SceneImageModelFamily.Api, PromptDialect = SceneImagePromptDialect.NaturalLanguage, MinChars = 150, MaxChars = 1200, MaxTokens = 256, RequiredComponentsJson = """["subject","action","framing","lighting"]""", ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame"]""", PoseInText = ImagePoseInText.Full, Negative = string.Empty, SettingsEnvelopeJson = "{}", ResearchSource = "FLUX family natural language; no negatives (BFL). PROVISIONAL — B135-037." },
        new() { Id = "profile-google-flash-image-3-1", CheckpointIdentifier = "google/flash-image-3.1", DisplayName = "Google Flash Image 3.1", Family = SceneImageModelFamily.Api, PromptDialect = SceneImagePromptDialect.NaturalLanguage, MinChars = 100, MaxChars = 1000, MaxTokens = 200, RequiredComponentsJson = """["subject","action","framing","lighting"]""", ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame"]""", PoseInText = ImagePoseInText.Full, Negative = string.Empty, SettingsEnvelopeJson = "{}", ResearchSource = "No published prompting guide captured yet. PROVISIONAL — B135-037." },
        new() { Id = "profile-qwen-image-2-0-pro", CheckpointIdentifier = "Qwen/Qwen-Image-2.0-Pro", DisplayName = "Qwen Image 2.0 Pro", Family = SceneImageModelFamily.Api, PromptDialect = SceneImagePromptDialect.NaturalLanguage, MinChars = 300, MaxChars = 1600, MaxTokens = 300, RequiredComponentsJson = """["subject","action","setting","framing","lighting"]""", ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame"]""", PoseInText = ImagePoseInText.Full, Negative = string.Empty, SettingsEnvelopeJson = "{}", ResearchSource = "Qwen image family reads long descriptive prompts (operator statement 2026-09-29). PROVISIONAL — B135-037." },
        new() { Id = "profile-google-imagen-4", CheckpointIdentifier = "google/imagen-4.0-preview", DisplayName = "Imagen 4.0 (preview)", Family = SceneImageModelFamily.Api, PromptDialect = SceneImagePromptDialect.NaturalLanguage, MinChars = 100, MaxChars = 1000, MaxTokens = 200, RequiredComponentsJson = """["subject","action","framing","lighting"]""", ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame"]""", PoseInText = ImagePoseInText.Full, Negative = string.Empty, SettingsEnvelopeJson = "{}", ResearchSource = "No published prompting guide captured yet. PROVISIONAL — B135-037." },
        new() { Id = "profile-openai-gpt-image-2", CheckpointIdentifier = "openai/gpt-image-2", DisplayName = "GPT-Image-2", Family = SceneImageModelFamily.Api, PromptDialect = SceneImagePromptDialect.NaturalLanguage, MinChars = 100, MaxChars = 1000, MaxTokens = 200, RequiredComponentsJson = """["subject","action","framing","lighting"]""", ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame"]""", PoseInText = ImagePoseInText.Full, Negative = string.Empty, SettingsEnvelopeJson = "{}", ResearchSource = "ApiSceneImagePromptCompiler (repo: no checkpoint-prompt dialect, no deterministic negative). PROVISIONAL — B135-037." },
    ];
}
