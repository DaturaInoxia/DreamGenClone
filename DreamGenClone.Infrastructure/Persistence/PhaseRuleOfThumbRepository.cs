namespace DreamGenClone.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed implementation of <see cref="IPhaseRuleOfThumbRepository"/>.
/// </summary>
public sealed class PhaseRuleOfThumbRepository : IPhaseRuleOfThumbRepository
{
    private readonly string _connectionString;

    public PhaseRuleOfThumbRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<PhaseRuleOfThumbRow?> GetByPhaseAsync(string phase, CancellationToken ct = default)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Phase, RuleOfThumbText FROM PhaseRuleOfThumb WHERE Phase = $phase LIMIT 1;";
        command.Parameters.AddWithValue("$phase", phase);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new PhaseRuleOfThumbRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2));
    }
}
