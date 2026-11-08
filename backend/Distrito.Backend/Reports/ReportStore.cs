using Distrito.Shared.Api;
using Npgsql;

namespace Distrito.Backend.Reports;

/// <summary>Reportes de problemas de la prueba cerrada: se guardan y el panel de administración muestra los últimos.</summary>
public sealed class ReportStore(NpgsqlDataSource db)
{
    public async Task<long> AddAsync(BugReport r, CancellationToken ct)
    {
        var text = r.Text.Trim();
        if (text.Length == 0)
            throw new ArgumentException("El reporte está vacío.");
        if (text.Length > BugReport.MaxLength)
            text = text[..BugReport.MaxLength];
        await using var cmd = db.CreateCommand("""
            INSERT INTO distrito.bug_reports (character_id, player, text, x, z, zone, version)
            VALUES (@c, @p, @t, @x, @z, @zone, @v) RETURNING id
            """);
        cmd.Parameters.AddWithValue("c", (object?)r.CharacterId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p", r.Player.Length > 60 ? r.Player[..60] : r.Player);
        cmd.Parameters.AddWithValue("t", text);
        cmd.Parameters.AddWithValue("x", r.X);
        cmd.Parameters.AddWithValue("z", r.Z);
        cmd.Parameters.AddWithValue("zone", r.Zone);
        cmd.Parameters.AddWithValue("v", r.Version);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<List<BugReport>> LatestAsync(int limit, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("""
            SELECT id, created_at, character_id, player, text, x, z, zone, version
              FROM distrito.bug_reports ORDER BY created_at DESC, id DESC LIMIT @l
            """);
        cmd.Parameters.AddWithValue("l", Math.Clamp(limit, 1, 200));
        var list = new List<BugReport>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new BugReport(r.IsDBNull(2) ? null : r.GetGuid(2), r.GetString(3), r.GetString(4), r.GetFloat(5), r.GetFloat(6),
                r.GetString(7), r.GetString(8), r.GetInt64(0), r.GetFieldValue<DateTimeOffset>(1)));
        return list;
    }
}
