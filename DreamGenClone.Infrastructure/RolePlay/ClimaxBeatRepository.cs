using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ClimaxBeatRepository : IClimaxBeatRepository
{
    private readonly PersistenceOptions _options;
    private readonly ILogger<ClimaxBeatRepository> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<ClimaxBeatEntry>? _cache;

    public ClimaxBeatRepository(IOptions<PersistenceOptions> options, ILogger<ClimaxBeatRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ClimaxBeatEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is not null)
            return _cache;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null)
                return _cache;

            var entries = await LoadAllFromDbAsync(cancellationToken);
            _cache = entries;
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ClimaxBeatEntry?> GetByCodeAsync(string beatCode, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(e => e.BeatCode == beatCode);
    }

    public async Task SaveAsync(ClimaxBeatEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ClimaxBeatEntries (BeatCode, StageNumber, StageName, SubBeatName, HintsJson, NextBeatCode, MinTurnsBeforeAdvance)
            VALUES ($beatCode, $stageNumber, $stageName, $subBeatName, $hintsJson, $nextBeatCode, $minTurns)
            ON CONFLICT(BeatCode) DO UPDATE SET
                StageNumber            = excluded.StageNumber,
                StageName              = excluded.StageName,
                SubBeatName            = excluded.SubBeatName,
                HintsJson              = excluded.HintsJson,
                NextBeatCode           = excluded.NextBeatCode,
                MinTurnsBeforeAdvance  = excluded.MinTurnsBeforeAdvance;
            """;

        command.Parameters.AddWithValue("$beatCode", entry.BeatCode);
        command.Parameters.AddWithValue("$stageNumber", (int)entry.StageNumber);
        command.Parameters.AddWithValue("$stageName", entry.StageName);
        command.Parameters.AddWithValue("$subBeatName", entry.SubBeatName);
        command.Parameters.AddWithValue("$hintsJson", JsonSerializer.Serialize(entry.Hints));
        command.Parameters.AddWithValue("$nextBeatCode", (object?)entry.NextBeatCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$minTurns", entry.MinTurnsBeforeAdvance);

        await command.ExecuteNonQueryAsync(cancellationToken);
        InvalidateCache();
        _logger.LogInformation("ClimaxBeatEntry saved: {BeatCode} — {SubBeatName}", entry.BeatCode, entry.SubBeatName);
    }

    public async Task DeleteAsync(string beatCode, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ClimaxBeatEntries WHERE BeatCode = $beatCode";
        command.Parameters.AddWithValue("$beatCode", beatCode);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        InvalidateCache();
        _logger.LogInformation("ClimaxBeatEntry deleted: {BeatCode}, RowsAffected={Rows}", beatCode, rows);
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM ClimaxBeatEntries";
        var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync(cancellationToken));
        if (count > 0)
            return;

        _logger.LogInformation("Seeding default ClimaxBeatEntries (32 sub-beats)");
        foreach (var entry in DefaultBeats)
            await InsertEntryAsync(connection, entry, cancellationToken);

        InvalidateCache();
    }

    public async Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM ClimaxBeatEntries";
        await deleteCmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Resetting ClimaxBeatEntries to defaults (32 sub-beats)");
        foreach (var entry in DefaultBeats)
            await InsertEntryAsync(connection, entry, cancellationToken);

        InvalidateCache();
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private void InvalidateCache()
    {
        _lock.Wait();
        try { _cache = null; }
        finally { _lock.Release(); }
    }

    private async Task<List<ClimaxBeatEntry>> LoadAllFromDbAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BeatCode, StageNumber, StageName, SubBeatName, HintsJson, NextBeatCode, MinTurnsBeforeAdvance
            FROM ClimaxBeatEntries
            ORDER BY BeatCode;
            """;

        var results = new List<ClimaxBeatEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ClimaxBeatEntry
            {
                BeatCode              = reader.GetString(0),
                StageNumber           = (byte)reader.GetInt32(1),
                StageName             = reader.GetString(2),
                SubBeatName           = reader.GetString(3),
                Hints                 = JsonSerializer.Deserialize<List<string>>(reader.GetString(4)) ?? [],
                NextBeatCode          = reader.IsDBNull(5) ? null : reader.GetString(5),
                MinTurnsBeforeAdvance = reader.GetInt32(6)
            });
        }

        return results;
    }

    private static async Task InsertEntryAsync(SqliteConnection connection, ClimaxBeatEntry entry, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ClimaxBeatEntries (BeatCode, StageNumber, StageName, SubBeatName, HintsJson, NextBeatCode, MinTurnsBeforeAdvance)
            VALUES ($beatCode, $stageNumber, $stageName, $subBeatName, $hintsJson, $nextBeatCode, $minTurns)
            ON CONFLICT(BeatCode) DO NOTHING;
            """;

        command.Parameters.AddWithValue("$beatCode", entry.BeatCode);
        command.Parameters.AddWithValue("$stageNumber", (int)entry.StageNumber);
        command.Parameters.AddWithValue("$stageName", entry.StageName);
        command.Parameters.AddWithValue("$subBeatName", entry.SubBeatName);
        command.Parameters.AddWithValue("$hintsJson", JsonSerializer.Serialize(entry.Hints));
        command.Parameters.AddWithValue("$nextBeatCode", (object?)entry.NextBeatCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$minTurns", entry.MinTurnsBeforeAdvance);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Canonical 32 sub-beats (seed data)
    // -----------------------------------------------------------------------

    private static readonly IReadOnlyList<ClimaxBeatEntry> DefaultBeats =
    [
        // Stage 1 — Clothed Contact
        new() { BeatCode = "1a", StageNumber = 1, StageName = "Clothed Contact", SubBeatName = "Pre-touch tension",
            Hints = ["Eye contact that lingers past comfort", "Noticing each other's breathing, proximity, warmth", "Bodies close without touching — the charged air between them"],
            NextBeatCode = "1b", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "1b", StageNumber = 1, StageName = "Clothed Contact", SubBeatName = "First contact (clothed)",
            Hints = ["Deliberate hand brush — fingertips on forearm or shoulder", "Cupping the side of the face, thumb tracing the jawline", "Tension of the first intentional touch through fabric"],
            NextBeatCode = "1c", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "1c", StageNumber = 1, StageName = "Clothed Contact", SubBeatName = "First kiss",
            Hints = ["Hesitant — barely touching lips, questioning", "Pressing firmer — learning each other's mouths", "Parting lips, first taste; deepening into open-mouthed kissing"],
            NextBeatCode = "1d", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "1d", StageNumber = 1, StageName = "Clothed Contact", SubBeatName = "Kissing elsewhere (clothed)",
            Hints = ["Jawline, neck, below the ear — light then sucking", "Trail of kisses along the throat", "Nipping the earlobe; breath moving over skin"],
            NextBeatCode = "1e", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "1e", StageNumber = 1, StageName = "Clothed Contact", SubBeatName = "Hands roaming over clothing",
            Hints = ["Hand on waist, hip, lower back — pressing bodies together", "Fingers tracing the spine through fabric", "Sliding along the ribcage, outer thigh"],
            NextBeatCode = "1f", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "1f", StageNumber = 1, StageName = "Clothed Contact", SubBeatName = "Groping through clothing",
            Hints = ["Cupping breast or groin through fabric — feeling the shape", "Grabbing buttocks; grinding palm against the hardness", "Thigh pressing between legs — friction and pressure through clothing"],
            NextBeatCode = "1g", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "1g", StageNumber = 1, StageName = "Clothed Contact", SubBeatName = "Pressing / pinning",
            Hints = ["Pressed against wall or pinned on a bed — weight and hold", "Pulled into lap; grinding while still clothed", "One person above, one below — bodies fully in contact"],
            NextBeatCode = "2a", MinTurnsBeforeAdvance = 1 },

        // Stage 2 — Undressing
        new() { BeatCode = "2a", StageNumber = 2, StageName = "Undressing", SubBeatName = "Shifted / partially removed",
            Hints = ["Shirt pulled up, exposing the stomach — paused there", "Straps pulled off shoulders, pant waistband tugged", "Clothing pushed aside or raised — a tease, not yet fully removed"],
            NextBeatCode = "2b", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "2b", StageNumber = 2, StageName = "Undressing", SubBeatName = "Tops fully removed",
            Hints = ["Shirt removed — the look that comes after", "Bra unhooked — moment of pause before it falls", "First full view of each other's bare upper bodies"],
            NextBeatCode = "2c", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "2c", StageNumber = 2, StageName = "Undressing", SubBeatName = "Kissing bare skin (upper body)",
            Hints = ["Kissing shoulders, collarbone, sternum", "First kiss on the nipple; licking then sucking", "Tongue tracing ribs, stomach, hip bones — moving downward"],
            NextBeatCode = "2d", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "2d", StageNumber = 2, StageName = "Undressing", SubBeatName = "Lower body undressing",
            Hints = ["Pants unbuttoned slowly; zipper lowered", "Underwear visible — brief touch through it before removal", "Last piece removed — both fully bare, first look"],
            NextBeatCode = "2e", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "2e", StageNumber = 2, StageName = "Undressing", SubBeatName = "Kissing bare skin (lower body)",
            Hints = ["Kissing inner thighs from the knee upward", "Kissing hip crease and the line below the navel", "Mouth near but not yet touching genitals — intentional tease"],
            NextBeatCode = "3a", MinTurnsBeforeAdvance = 1 },

        // Stage 3 — Intimate Touch
        new() { BeatCode = "3a", StageNumber = 3, StageName = "Intimate Touch", SubBeatName = "Female touching male — initial",
            Hints = ["First contact — fingertips grazing the length", "First grip wrapping around the shaft — feeling warmth and hardness", "First slow stroke — adjusting grip to his responses"],
            NextBeatCode = "3b", MinTurnsBeforeAdvance = 2 },

        new() { BeatCode = "3b", StageNumber = 3, StageName = "Intimate Touch", SubBeatName = "Male touching female — initial",
            Hints = ["First contact — palm flat, then fingertips tracing outer labia", "Parting the labia — first feel of her wetness", "Clitoris found — first slow circling touch, very light"],
            NextBeatCode = "3c", MinTurnsBeforeAdvance = 2 },

        new() { BeatCode = "3c", StageNumber = 3, StageName = "Intimate Touch", SubBeatName = "Reciprocal simultaneous touching",
            Hints = ["Both reaching for each other at the same time", "Adjusting bodies so both can reach comfortably", "Her hand on him while his fingers work on her — independent rhythms"],
            NextBeatCode = "4a", MinTurnsBeforeAdvance = 1 },

        // Stage 4 — Sustained Manual
        new() { BeatCode = "4a", StageNumber = 4, StageName = "Sustained Manual", SubBeatName = "Sustained handjob",
            Hints = ["Consistent rhythm established; grip tightens on the upstroke", "Twist motion, focus on the head and frenulum", "Edging — stopping at the edge, letting him settle, building again"],
            NextBeatCode = "4b", MinTurnsBeforeAdvance = 2 },

        new() { BeatCode = "4b", StageNumber = 4, StageName = "Sustained Manual", SubBeatName = "Sustained fingering",
            Hints = ["Consistent rhythm — fingers curling to hit the spot every stroke", "Thumb circling clitoris in matching rhythm", "Feeling her tighten and tremble; edging before orgasm"],
            NextBeatCode = "4c", MinTurnsBeforeAdvance = 2 },

        new() { BeatCode = "4c", StageNumber = 4, StageName = "Sustained Manual", SubBeatName = "Grinding — bare skin",
            Hints = ["Bodies pressed together — his penis between her thighs, not inside", "Rubbing against her vulva — slick sliding, wetness coating both", "Hips rolling — chasing friction, teasing at the entrance"],
            NextBeatCode = "4d", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "4d", StageNumber = 4, StageName = "Sustained Manual", SubBeatName = "Double stimulation",
            Hints = ["Fingers inside and mouth on nipples", "Hand on clitoris and mouth on her neck simultaneously", "Hand on penis and mouth on his chest at the same time"],
            NextBeatCode = "5a", MinTurnsBeforeAdvance = 1 },

        // Stage 5 — Oral (Initial)
        new() { BeatCode = "5a", StageNumber = 5, StageName = "Oral — Initial", SubBeatName = "Male performing on female — first contact",
            Hints = ["Kissing from inner thigh upward; first warm breath on her vulva", "Broad flat tongue — first long slow lick, bottom to top, tasting her", "Finding the clitoris with the tongue — first light direct contact"],
            NextBeatCode = "5b", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "5b", StageNumber = 5, StageName = "Oral — Initial", SubBeatName = "Female performing on male — first contact",
            Hints = ["Kissing along the shaft; first broad-tongue lick from base to tip", "Tongue circling the head; tasting pre-cum", "Lips sealing around the head — first suction, tongue swirling"],
            NextBeatCode = "6a", MinTurnsBeforeAdvance = 1 },

        // Stage 6 — Oral (Building)
        new() { BeatCode = "6a", StageNumber = 6, StageName = "Oral — Building", SubBeatName = "Male performing on female — building",
            Hints = ["Rhythm established — flat tongue alternating with pointed tip on the clitoris", "Suction: pulling clitoris into mouth, rhythmic in-and-out with lips", "Fingers joining — curling inside while tongue works the clitoris"],
            NextBeatCode = "6b", MinTurnsBeforeAdvance = 2 },

        new() { BeatCode = "6b", StageNumber = 6, StageName = "Oral — Building", SubBeatName = "Female performing on male — building",
            Hints = ["Rhythm: mouth moving up and down, hand stroking what mouth can't reach", "Suction tight on the upstroke; tongue active on the downstroke", "Vary deep-slow with shallow-fast; pulling off to lick base-to-tip then re-engulfing"],
            NextBeatCode = "7a", MinTurnsBeforeAdvance = 2 },

        // Stage 7 — Oral (Intense)
        new() { BeatCode = "7a", StageNumber = 7, StageName = "Oral — Intense", SubBeatName = "Male performing on female — intense",
            Hints = ["Relentless tongue on clitoris — not stopping through her squirming", "Fingers thrusting hard; sustained suction with mouth sealed", "Her body tensing, sounds escalating; pressing harder through her climax"],
            NextBeatCode = "7b", MinTurnsBeforeAdvance = 2 },

        new() { BeatCode = "7b", StageNumber = 7, StageName = "Oral — Intense", SubBeatName = "Female performing on male — intense",
            Hints = ["Deeper, faster — mouth and hand in tandem, suction tight", "Feeling him pulse; his hips lifting toward her mouth", "Edging — pulling off before he peaks, then resuming; no male climax yet"],
            NextBeatCode = "8a", MinTurnsBeforeAdvance = 2 },

        // Stage 8 — Penetrative
        new() { BeatCode = "8a", StageNumber = 8, StageName = "Penetrative", SubBeatName = "Positioning",
            Hints = ["Shifting bodies — choosing position, who leads and follows", "Lining up — the tip at the entrance, first contact at the opening"],
            NextBeatCode = "8b", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "8b", StageNumber = 8, StageName = "Penetrative", SubBeatName = "First entry",
            Hints = ["Pushing in — first inch, pausing; feeling the stretch and fullness", "Sinking deeper — slow, inch by inch until fully inside", "Held still — both absorbing the sensation before movement begins"],
            NextBeatCode = "8c", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "8c", StageNumber = 8, StageName = "Penetrative", SubBeatName = "Establishing rhythm",
            Hints = ["Shallow strokes first — short and gentle, then lengthening", "Finding the angle that hits right; rolling hips while fully inside"],
            NextBeatCode = "8d", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "8d", StageNumber = 8, StageName = "Penetrative", SubBeatName = "Position changes during penetration",
            Hints = ["Rolling to change who is on top; shifting legs — wrapped around, spread wide", "Flipping from behind — withdrawing and re-entering at new angle", "Her on top riding; from behind with hands on hips"],
            NextBeatCode = "8e", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "8e", StageNumber = 8, StageName = "Penetrative", SubBeatName = "Building intensity",
            Hints = ["Faster, harder — urgency increasing, deeper thrusts", "Adding manual stimulation — finger on clitoris during penetration", "Audible skin and wet sounds; furniture shifting"],
            NextBeatCode = "8f", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "8f", StageNumber = 8, StageName = "Penetrative", SubBeatName = "Verbal and vocal escalation",
            Hints = ["Gasps, moans, grunts; saying each other's names", "Instructions and praise — 'harder,' 'right there,' 'don't stop,' 'you feel amazing'", "Raw explicit language; whimpering, crying out, breath catching"],
            NextBeatCode = "8g", MinTurnsBeforeAdvance = 1 },

        new() { BeatCode = "8g", StageNumber = 8, StageName = "Penetrative", SubBeatName = "Sustained",
            Hints = ["Continuing through her orgasm — not stopping, keeping rhythm", "Changing pace — slow momentarily then rebuild", "Sustaining until /endclimax is submitted"],
            NextBeatCode = null, MinTurnsBeforeAdvance = int.MaxValue },
    ];
}
