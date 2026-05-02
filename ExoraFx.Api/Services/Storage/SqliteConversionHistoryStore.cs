using ExoraFx.Api.Models;
using ExoraFx.Api.Persistence;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text;

namespace ExoraFx.Api.Services.Storage;

public sealed class SqliteConversionHistoryStore(IDbConnectionFactory factory) : IConversionHistoryStore
{
    public long Append(long userId, string? userName, string userRole, ConvertResult result)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO conversion_history
                (user_id, user_name, user_role, from_currency, to_currency, from_amount, to_amount, bank, margin_percent, profit_uah, profit_eur, created_at_utc, state)
            VALUES
                ($id, $name, $role, $from, $to, $fromA, $toA, $bank, $margin, $profitUah, $profitEur, $created, $state);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$id", userId);
        command.Parameters.AddWithValue("$name", (object?)userName ?? DBNull.Value);
        command.Parameters.AddWithValue("$role", userRole);
        command.Parameters.AddWithValue("$from", result.From);
        command.Parameters.AddWithValue("$to", result.To);
        command.Parameters.AddWithValue("$fromA", (double)result.FromAmount);
        command.Parameters.AddWithValue("$toA", (double)result.ToAmount);
        command.Parameters.AddWithValue("$bank", result.Bank);
        command.Parameters.AddWithValue("$margin", (double)result.MarginPercent);
        command.Parameters.AddWithValue("$profitUah", (double)result.ProfitUah);
        command.Parameters.AddWithValue("$profitEur", result.ProfitEur is { } eur ? (double)eur : (object)DBNull.Value);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$state", HistoryState.Draft);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<HistoryEntry> GetPage(long userId, int offset, int take)
    {
        if (take <= 0)
            return [];

        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, user_id, user_name, user_role, from_currency, to_currency, from_amount, to_amount, bank, margin_percent, profit_uah, profit_eur, created_at_utc, state
            FROM conversion_history
            WHERE user_id = $id
            ORDER BY id DESC
            LIMIT $take OFFSET $offset
            """;
        command.Parameters.AddWithValue("$id", userId);
        command.Parameters.AddWithValue("$take", take);
        command.Parameters.AddWithValue("$offset", offset < 0 ? 0 : offset);

        using var reader = command.ExecuteReader();
        var rows = new List<HistoryEntry>(take);
        while (reader.Read())
            rows.Add(ReadEntry(reader));

        return rows;
    }

    public int Count(long userId)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM conversion_history WHERE user_id = $id";
        command.Parameters.AddWithValue("$id", userId);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public string? ToggleState(long userId, long entryId)
    {
        using var connection = factory.Open();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT state FROM conversion_history WHERE id = $eid AND user_id = $uid";
        read.Parameters.AddWithValue("$eid", entryId);
        read.Parameters.AddWithValue("$uid", userId);
        if (read.ExecuteScalar() is not string current)
            return null;

        var next = current == HistoryState.Done ? HistoryState.Draft : HistoryState.Done;
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE conversion_history SET state = $state WHERE id = $eid AND user_id = $uid";
        update.Parameters.AddWithValue("$state", next);
        update.Parameters.AddWithValue("$eid", entryId);
        update.Parameters.AddWithValue("$uid", userId);
        update.ExecuteNonQuery();
        return next;
    }

    public HistoryEntry? GetById(long userId, long entryId)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, user_id, user_name, user_role, from_currency, to_currency, from_amount, to_amount, bank, margin_percent, profit_uah, profit_eur, created_at_utc, state
            FROM conversion_history WHERE id = $eid AND user_id = $uid
            """;
        command.Parameters.AddWithValue("$eid", entryId);
        command.Parameters.AddWithValue("$uid", userId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    public HistoryStats GetStats(long userId, DateTime? sinceUtc = null, DateTime? untilUtc = null)
    {
        using var connection = factory.Open();

        var totalCount = 0;
        var doneCount = 0;
        var draftCount = 0;
        DateTime? firstAt = null;
        DateTime? lastAt = null;
        var paidUah = 0m;
        var profitUah = 0m;
        var profitEur = 0m;
        decimal marginSum = 0m;
        decimal maxTradeUah = 0m;
        string? maxTradeCur = null;
        decimal maxTradeFromAmount = 0m;
        var received = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var bankCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var (whereClause, parameters) = BuildPeriodFilter(userId, sinceUtc, untilUtc);

        using (var rows = connection.CreateCommand())
        {
            rows.CommandText =
                $"""
                SELECT from_currency, to_currency, from_amount, to_amount, profit_uah, profit_eur, margin_percent, created_at_utc, bank, state
                FROM conversion_history WHERE {whereClause}
                """;
            foreach (var (k, v) in parameters)
                rows.Parameters.AddWithValue(k, v);
            using var r = rows.ExecuteReader();
            while (r.Read())
            {
                var fromCur = r.GetString(0).ToLowerInvariant();
                var toCur = r.GetString(1).ToLowerInvariant();
                var fromAmt = r.GetDecimal(2);
                var toAmt = r.GetDecimal(3);
                profitUah += r.GetDecimal(4);
                if (!r.IsDBNull(5))
                    profitEur += r.GetDecimal(5);
                marginSum += r.GetDecimal(6);
                var createdAt = DateTime.Parse(r.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                var bank = r.GetString(8);
                var state = r.GetString(9);

                totalCount++;
                if (state == HistoryState.Done) doneCount++;
                else draftCount++;

                if (firstAt is null || createdAt < firstAt) firstAt = createdAt;
                if (lastAt is null || createdAt > lastAt) lastAt = createdAt;

                if (fromCur != "uah")
                    received[fromCur] = received.GetValueOrDefault(fromCur) + fromAmt;
                else
                    paidUah += fromAmt;

                if (toCur != "uah")
                    received[toCur] = received.GetValueOrDefault(toCur) + toAmt;
                else
                    paidUah += toAmt;

                var uahForTrade = toCur == "uah" ? toAmt : fromCur == "uah" ? fromAmt : 0m;
                if (uahForTrade > maxTradeUah)
                {
                    maxTradeUah = uahForTrade;
                    maxTradeCur = fromCur != "uah" ? fromCur : toCur;
                    maxTradeFromAmount = fromCur != "uah" ? fromAmt : toAmt;
                }

                bankCounts[bank] = bankCounts.GetValueOrDefault(bank) + 1;
            }
        }

        if (totalCount == 0)
            return new HistoryStats(0, 0, 0, null, null, received, 0m, 0m, 0m, 0m, null, 0, null, 0m, null, 0m);

        var topBankPair = bankCounts.OrderByDescending(kv => kv.Value).FirstOrDefault();
        var avgMargin = marginSum / totalCount;
        var topCurrency = received.Count == 0 ? null : received.OrderByDescending(kv => kv.Value).First().Key;
        return new HistoryStats(
            totalCount, doneCount, draftCount, firstAt, lastAt, received, paidUah, profitUah, profitEur,
            avgMargin, topBankPair.Key, topBankPair.Value, topCurrency, maxTradeUah, maxTradeCur, maxTradeFromAmount);
    }

    public void Clear(long userId)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM conversion_history WHERE user_id = $id";
        command.Parameters.AddWithValue("$id", userId);
        command.ExecuteNonQuery();
    }

    private static (string Where, List<KeyValuePair<string, object>> Params) BuildPeriodFilter(long userId, DateTime? sinceUtc, DateTime? untilUtc)
    {
        var where = new StringBuilder("user_id = $uid");
        var p = new List<KeyValuePair<string, object>> { new("$uid", userId) };
        if (sinceUtc is { } s)
        {
            where.Append(" AND created_at_utc >= $since");
            p.Add(new("$since", s.ToString("o", CultureInfo.InvariantCulture)));
        }
        if (untilUtc is { } u)
        {
            where.Append(" AND created_at_utc < $until");
            p.Add(new("$until", u.ToString("o", CultureInfo.InvariantCulture)));
        }
        return (where.ToString(), p);
    }

    private static HistoryEntry ReadEntry(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetInt64(1),
        r.IsDBNull(2) ? null : r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.GetString(4),
        r.GetString(5),
        r.GetDecimal(6),
        r.GetDecimal(7),
        r.GetString(8),
        r.GetDecimal(9),
        r.GetDecimal(10),
        r.IsDBNull(11) ? null : r.GetDecimal(11),
        DateTime.Parse(r.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        r.GetString(13));
}
