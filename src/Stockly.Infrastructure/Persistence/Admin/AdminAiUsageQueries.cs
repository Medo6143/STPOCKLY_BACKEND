using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Admin;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Platform;
using Stockly.Infrastructure.Persistence.Configurations;

namespace Stockly.Infrastructure.Persistence.Admin;

/// <summary>
/// Cross-tenant AI usage aggregates (AD14–AD16) over <c>[from, to)</c>, grouped in PostgreSQL. The range is served by
/// <c>ix_ai_usage_records_created_at</c>, or <c>ix_ai_usage_records_shop_id_created_at</c> with a shop. A call is a row
/// that is not a cache hit; a failure is such a row whose outcome is not <c>Succeeded</c>. Plain SQL through the
/// context's connection: the Cairo day is <c>(created_at AT TIME ZONE 'Africa/Cairo')::date</c>.
/// </summary>
internal sealed class AdminAiUsageQueries(StocklyDbContext db) : IAdminAiUsageQueries
{
    private const string Usage = AiUsageRecordConfiguration.TableName;
    private const string Shops = ShopConfiguration.TableName;
    private static readonly string Calls = "count(*) FILTER (WHERE NOT r.cache_hit)";
    private static readonly string Failures = string.Create(
        CultureInfo.InvariantCulture, $"count(*) FILTER (WHERE NOT r.cache_hit AND r.outcome <> {(short)AiCallOutcome.Succeeded})");
    private static readonly string Cost = "coalesce(sum(r.cost_usd), 0)";
    private static readonly string InRange = "r.created_at >= @from AND r.created_at < @to";

    public Task<IReadOnlyList<AiUsageDay>> DailyAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? shopId, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT (r.created_at AT TIME ZONE '{BusinessCalendar.TimeZoneId}')::date, {Calls},
                   count(*) FILTER (WHERE r.cache_hit), {Failures},
                   coalesce(sum(r.prompt_tokens), 0), coalesce(sum(r.completion_tokens), 0), {Cost}
            FROM {Usage} r
            WHERE {InRange}{(shopId is null ? string.Empty : " AND r.shop_id = @shop")}
            GROUP BY 1
            ORDER BY 1
            """;
        return QueryAsync(
            sql,
            Parameters(fromUtc, toUtc, shopId),
            reader => new AiUsageDay(
                reader.GetFieldValue<DateOnly>(0),
                ToInt(reader.GetInt64(1)),
                ToInt(reader.GetInt64(2)),
                ToInt(reader.GetInt64(3)),
                ToInt(reader.GetInt64(4)),
                ToInt(reader.GetInt64(5)),
                AdminMapping.Usd(reader.GetDecimal(6))),
            cancellationToken);
    }

    public async Task<PagedResponse<AiUsageByShop>> ByShopAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, int page, int pageSize, CancellationToken cancellationToken)
    {
        var count = await QueryAsync(
            $"SELECT count(DISTINCT r.shop_id) FROM {Usage} r WHERE {InRange}",
            Parameters(fromUtc, toUtc, null),
            reader => ToInt(reader.GetInt64(0)),
            cancellationToken);

        var parameters = Parameters(fromUtc, toUtc, null);
        parameters.Add(new NpgsqlParameter("take", pageSize));
        parameters.Add(new NpgsqlParameter("skip", (page - 1) * pageSize));
        var rows = await QueryAsync(
            $"""
            SELECT r.shop_id, s.name, {Calls}, {Failures}, {Cost} AS cost
            FROM {Usage} r JOIN {Shops} s ON s.id = r.shop_id
            WHERE {InRange}
            GROUP BY r.shop_id, s.name
            ORDER BY cost DESC, r.shop_id
            LIMIT @take OFFSET @skip
            """,
            parameters,
            reader => new AiUsageByShop(
                reader.GetGuid(0),
                reader.GetString(1),
                ToInt(reader.GetInt64(2)),
                ToInt(reader.GetInt64(3)),
                AdminMapping.Usd(reader.GetDecimal(4))),
            cancellationToken);

        return new PagedResponse<AiUsageByShop>(rows, page, pageSize, count.Count == 0 ? 0 : count[0]);
    }

    public Task<IReadOnlyList<AiUsageByModel>> ByModelAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        QueryAsync(
            $"""
            SELECT r.model, {Calls}, {Failures}, {Cost} AS cost,
                   coalesce(round(avg(r.latency_ms) FILTER (WHERE NOT r.cache_hit)), 0)
            FROM {Usage} r
            WHERE {InRange}
            GROUP BY r.model
            ORDER BY cost DESC, r.model
            """,
            Parameters(fromUtc, toUtc, null),
            reader => new AiUsageByModel(
                reader.GetString(0),
                ToInt(reader.GetInt64(1)),
                ToInt(reader.GetInt64(2)),
                AdminMapping.Usd(reader.GetDecimal(3)),
                (int)reader.GetDecimal(4)),
            cancellationToken);

    private static List<DbParameter> Parameters(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? shopId)
    {
        List<DbParameter> parameters = [new NpgsqlParameter("from", fromUtc), new NpgsqlParameter("to", toUtc)];
        if (shopId is { } shop)
        {
            parameters.Add(new NpgsqlParameter("shop", shop));
        }

        return parameters;
    }

    private static int ToInt(long value) => (int)Math.Min(value, int.MaxValue);

    // Runs on the context's connection (opened and closed by EF), so a test or a request shares it like any EF query.
    private async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql, IReadOnlyList<DbParameter> parameters, Func<DbDataReader, T> read, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(read(reader));
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
