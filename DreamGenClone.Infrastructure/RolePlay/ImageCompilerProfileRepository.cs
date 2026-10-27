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

    public async Task<ImageCompilerProfile?> FindByIdAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new InvalidOperationException("A profile id is required to resolve an image compiler profile.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Matched on Id, not on a checkpoint name: a suite cell pins the ROW it was compiled for, so a later edit to
        // that row's checkpoint identifier cannot silently retarget the cell at a different checkpoint's settings.
        command.CommandText = $"""
            SELECT {SelectColumns}
            FROM ImageCompilerProfiles
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", profileId.Trim());
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

        await MigrateBudgetTokenAsync(connection, cancellationToken);
        await SeedAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Converts the prompt budget baked into a stored compiler instruction into the single-owner token (B-167).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The instruction texts used to state a literal <c>800</c> characters, and rows were seeded with that sentence, so a
    /// checkpoint whose profile declares a different budget (Qwen-Image-2.1 declares 3400) was told one number while the
    /// profile enforced another — a cell that can never pass. The texts now carry
    /// <see cref="SceneImageCompilerSystemPrompts.BudgetToken"/> and the render-time resolution supplies the profile's
    /// own value.
    /// </para>
    /// <para>
    /// This rewrites ONLY that one sentence, and only where the old literal is still present, so an operator's edits to
    /// any other part of the instruction survive and a row already carrying the token is untouched. It is idempotent.
    /// </para>
    /// </remarks>
    private static async Task MigrateBudgetTokenAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ImageCompilerProfiles
            SET SystemPrompt = REPLACE(SystemPrompt, 'under 800 characters', 'under {{maxChars}} characters'),
                UpdatedUtc = $updatedUtc
            WHERE INSTR(SystemPrompt, 'under 800 characters') > 0;
            """;
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        foreach (var profile in SeedProfiles())
        {
            // B-135: the row carries its OWN copy of the compiler instructions, derived here from its family's
            // researched text (SceneImageCompilerSystemPrompts, which the prompt builders read too). This is a SEED,
            // not a read-path fallback: nothing at render time substitutes a missing value, and a row whose text is
            // later edited keeps that edit because the insert is DO NOTHING.
            profile.SystemPrompt = SceneImageCompilerSystemPrompts.For(profile.Family, canonical: false);

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
            // T5-XXL max_sequence_length = 512 (the HF card's reference diffusers call) — not the provisional 256.
            MaxTokens = 512,
            RequiredComponentsJson = """["subject","action","setting","framing","lighting"]""",
            ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame"]""",
            // Measured (B-116): a dictated two-person pose is FRAGILE in text-T2I on this checkpoint; geometry holds
            // only via an img2img re-skin over a pose base (B-119 route C3).
            PoseInText = ImagePoseInText.SimpleOnly,
            Negative = string.Empty,
            NegativeSource = null,
            SettingsEnvelopeJson = """{"guidance":3.5,"cfg":1.0,"sampler":"euler","scheduler":"simple","resolution":"1024x1024"}""",
            ResearchSource =
                "research/flux1-dev-prompting.md — black-forest-labs/FLUX.1-dev HF model card (fetched 2026-10-03): "
                + "12B rectified-flow transformer, guidance 3.5 / 50 steps / max_sequence_length 512 / 1024x1024, "
                + "no negative prompt (FluxPipeline takes none). B-112 (FluxGuidance 3.5 + cfg 1.0, euler/simple) + "
                + "B-116 (dictated two-person pose fragile in text; use the re-skin route).",
        },
        new()
        {
            Id = "profile-qwen-image-2-1",
            CheckpointIdentifier = "qwen_image_2.1_int8_convrot.safetensors",
            DisplayName = "Qwen-Image-2.1 (Local ComfyUI)",
            Family = SceneImageModelFamily.QwenImage21,
            PromptDialect = SceneImagePromptDialect.NaturalLanguage,
            // Qwen-2.1 wants LONG, detailed prompts WHEN THE PROMPT IS THE ONLY CARRIER (no references bound); with
            // identity/body/location plates bound the anchored contract applies and a short prompt that states every
            // remaining fact is CORRECT (vendor §5 "anchor on the image; nothing invented"). So the CAP stays generous
            // while the FLOOR comes down to the anchored regime: a 300-character floor rejected the operator's own
            // ~200-character working prompt and the model padded to reach it (measured 2026-10-25, SceneImage 3ade4908).
            MinChars = 120,
            MaxChars = 3400,
            // A long-form budget needs a matching token allowance: 300 tokens is ~1200 characters, so a 3400-character
            // answer could not be produced even if the character rule allowed it.
            MaxTokens = 700,
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
            // The Noct Q Uncensored build of Qwen-Image-2.1 (registered 2026-10-23). Same architecture, same prompt
            // dialect and same graph as the base row; only the DiT merge, the text encoder and the VAE differ, so the
            // checkpoint needs its OWN row (resolution is keyed on the checkpoint, never the family) carrying the same
            // researched Qwen-Image-2.1 rules. The identifier is the real DiT FILENAME the ComfyUI host loads - the
            // render path feeds it to UNETLoader verbatim and refuses anything that is not a checkpoint filename.
            Id = "profile-qwen-image-2-1-noct-q",
            CheckpointIdentifier = "noctQUncensoredRealismBuiltOn_v40Base_3260131.safetensors",
            DisplayName = "Qwen-Image-2.1 Noct Q Uncensored (Local ComfyUI)",
            Family = SceneImageModelFamily.QwenImage21,
            PromptDialect = SceneImagePromptDialect.NaturalLanguage,
            // Carried from profile-qwen-image-2-1, including the 120/3400/700 budget: this build runs the same
            // Qwen-Image-2.1 prompt dialect and the same two contracts (long-form when nothing is bound, anchored and
            // therefore potentially short when references carry the picture), so a different floor here would make the
            // anchored contract unsatisfiable on this checkpoint only.
            MinChars = 120,
            MaxChars = 3400,
            MaxTokens = 700,
            RequiredComponentsJson = """["subject","action","setting","framing","lighting"]""",
            ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame","pony-tag"]""",
            PoseInText = ImagePoseInText.Full,
            Negative = string.Empty,
            NegativeSource = null,
            // The Noct Q author's envelope, as recorded on the model row - NOT the base row's cfg 1, which would leave
            // this checkpoint's negative branch inert.
            SettingsEnvelopeJson = """{"steps":30,"cfg":3.0,"sampler":"euler","scheduler":"simple"}""",
            ResearchSource =
                "Qwen-Image-2.1 researched rules, cited in profile-qwen-image-2-1 (that row carries the architecture's "
                + "prompt dialect and prohibition set), applied unchanged here: this row is a Qwen-Image-2.1 derivative "
                + "that swaps only the DiT (Noct Q Uncensored Realism V4 Base int8 merge, Civitai 2958896 -> "
                + "noctQUncensoredRealismBuiltOn_v40Base_3260131.safetensors), the text encoder (Heretic fp8 uncensored "
                + "Qwen3-VL 8B -> qwen3vl_8b_fp8_heretic.safetensors) and the VAE (texture-fix bf16 -> "
                + "texture_fix_vae_for_qwen_image_2.1_bf16.safetensors). No Noct-Q specific prompting research exists, "
                + "so no prompt-shape claim is made for this build; the character budget and the token allowance are "
                + "carried from the base row. The envelope is the Noct Q author's stated 25-50 steps / euler / simple / "
                + "cfg 3, NOT the base row's cfg 1 - so the negative branch is live here, and this row still declares no "
                + "negative because none has been researched for it. Tighten in B135-037.",
        },
        new()
        {
            Id = "profile-krea2-turbo",
            CheckpointIdentifier = "krea2_turbo_fp8_scaled.safetensors",
            DisplayName = "Krea 2 Turbo (local ComfyUI)",
            Family = SceneImageModelFamily.Krea2,
            PromptDialect = SceneImagePromptDialect.Krea2NaturalLanguage,
            // A photographic BRIEF, not a long-form description and not tag soup. B-167: the compiler text no longer
            // states a number at all — it carries {{maxChars}} and the render resolves it from THIS row's MaxChars, so
            // the instruction and the enforced budget cannot disagree.
            MinChars = 120,
            // 800 was too small to state the still brief's facts once each. Measured 2026-10-25 (SceneImage 36857035,
            // Dean POV): with the ceiling actually enforced for the first time, the compiler produced an 824-character
            // caption and paid for it by deleting the NUDITY clause — the one fact nothing else in the record repeats.
            // The facts a participant frame must carry (place, light, event, posture, facing, action, wardrobe, the
            // camera holder's visible body, the frame) need ~1100-1300 characters, so the ceiling is 1600.
            MaxChars = 1600,
            // The text encoder is Qwen3-VL 4B, NOT CLIP: the SDXL-family 75-token CLIP window is a property of a
            // different architecture and would be simply wrong here. 1600 characters is roughly 400 tokens, so the
            // allowance is 600 — a ceiling the allowance cannot reach is a ceiling the model meets by deleting facts.
            MaxTokens = 600,
            RequiredComponentsJson = """["subject","action","setting","lighting","photographic-treatment"]""",
            // The last three are this checkpoint's measured traps, spelled out so the categories are self-describing
            // rather than needing the proof matrix to interpret: an explicit framing DEMAND never once worked, a body
            // noun-list loses the head, and a face-facing clause on an act suppresses the act.
            ForbiddenTokensJson = """["story-name","relationship","ownership","negation","pov-character-in-frame","pony-tag","framing-demand","body-noun-list","face-facing-clause"]""",
            // Acts ARE described in text successfully on this checkpoint: the 59-cell matrix renders explicit
            // penetration and oral from prose, including two people. That is the OPPOSITE of BigLust/Juggernaut/FLUX
            // (Forbidden/SimpleOnly) and it is a measured property of the checkpoint, not a preference.
            PoseInText = ImagePoseInText.Full,
            // Krea-2 Turbo takes NO negative text at all: its graph feeds the sampler a ConditioningZeroOut of the
            // POSITIVE, so a negative would have nowhere to go. Empty here is the architecture, not a style choice.
            Negative = string.Empty,
            NegativeSource = null,
            // DOCUMENTATION ONLY. The render reads the envelope from the model's own TextToImage qualification
            // (Krea2Refs: unet/clip/vae/steps/cfg/sampler/scheduler/denoise), which is the single source the graph
            // builder consults. This copy is recorded here so the profile describes its checkpoint like every other
            // row does - nothing reads it, and nothing should start to.
            SettingsEnvelopeJson = """{"steps":8,"cfg":1.0,"sampler":"euler","scheduler":"simple","denoise":1.0,"resolution":"1024x1024"}""",
            ResearchSource =
                "helpers/local-comfyui-host/run-krea2-proof.ps1 - the 59-cell proof matrix on this checkpoint, reviewed "
                + "2026-10-01 (specs/Planning/B-137-krea2-local-generation): stock weights + stock text encoder render full "
                + "nudity with NO LoRA; the prompt SHAPE is the largest quality lever (a photographic brief returns a "
                + "complete figure where a body noun-list returns a torso crop, and an explicit framing demand never "
                + "worked in any cell). Envelope from the model's qualified TextToImage entry, NOT from this row.",
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
