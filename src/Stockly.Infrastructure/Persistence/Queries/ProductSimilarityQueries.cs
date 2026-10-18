using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;

namespace Stockly.Infrastructure.Persistence.Queries;

/// <summary>
/// The trigram lookup of <c>TrigramMatchStrategy</c> (docs/erd.md §3.2, §3.5): one statement for all lines. Raw SQL
/// because <c>similarity()</c> and <c>%</c> have no EF translation without an extra package, so the tenant condition is
/// explicit (<c>shop_id = @shop</c> on every table) and an absent tenant returns nothing (fail closed, ADR-0002).
/// </summary>
internal sealed class ProductSimilarityQueries(StocklyDbContext db, ICurrentTenant currentTenant) : IProductSimilarityQueries
{
    // Per line (unnest of the parallel arrays), a LATERAL subquery takes the most similar candidates:
    //  - products: ix_products_normalized_name_trgm / ix_products_latin_key_trgm through "%" (GIN, gin_trgm_ops);
    //  - aliases of every supplier: ix_product_aliases_normalized_alias_trgm / ix_product_aliases_latin_key_trgm, joined
    //    to the non-archived product by primary key.
    // "%" uses pg_trgm.similarity_threshold (0.3 by default) as an index-backed pre-filter; the exact threshold (>= 0.3,
    // MatchingOptions) is applied to the higher of the two similarities. Units of name candidates come from the
    // product_units (product_id) index in the same statement.
    private const string Sql = """
        SELECT l.line_no AS line_no, c.product_id AS product_id, c.alias_unit_id AS alias_unit_id,
               c.other_supplier AS other_supplier, c.similarity AS similarity,
               u.id AS unit_id, u.name AS unit_name, u.is_base AS unit_is_base
        FROM unnest(@line_nos, @names, @latin_keys) AS l(line_no, name, latin_key)
        CROSS JOIN LATERAL (
            SELECT s.product_id, s.alias_unit_id, s.other_supplier, s.similarity
            FROM (
                SELECT p.id AS product_id, NULL::uuid AS alias_unit_id, false AS other_supplier,
                       GREATEST(similarity(p.normalized_name, l.name), similarity(p.latin_key, l.latin_key)) AS similarity
                FROM products p
                WHERE p.shop_id = @shop AND NOT p.is_archived
                  AND (p.normalized_name % l.name OR p.latin_key % l.latin_key)
                UNION ALL
                SELECT a.product_id, a.product_unit_id, a.supplier_id IS DISTINCT FROM @supplier,
                       GREATEST(similarity(a.normalized_alias, l.name), similarity(a.latin_key, l.latin_key))
                FROM product_aliases a
                JOIN products p ON p.id = a.product_id AND p.shop_id = @shop AND NOT p.is_archived
                WHERE a.shop_id = @shop
                  AND (a.normalized_alias % l.name OR a.latin_key % l.latin_key)
            ) s
            WHERE s.similarity >= @threshold
            ORDER BY s.similarity DESC, s.product_id, s.alias_unit_id
            LIMIT @max_candidates
        ) c
        LEFT JOIN product_units u ON c.alias_unit_id IS NULL AND u.product_id = c.product_id AND u.shop_id = @shop
        ORDER BY l.line_no, c.similarity DESC, c.product_id, c.alias_unit_id, u.id
        """;

    public async Task<IReadOnlyList<SimilarCandidate>> FindSimilarAsync(
        Guid? supplierId, IReadOnlyList<SimilarityProbe> probes, double threshold, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probes);
        if (probes.Count == 0 || currentTenant.ShopId is not { } shopId)
        {
            return [];
        }

        object[] parameters =
        [
            new NpgsqlParameter("line_nos", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = probes.Select(probe => probe.LineNo).ToArray() },
            new NpgsqlParameter("names", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = probes.Select(probe => probe.NormalizedName).ToArray() },
            new NpgsqlParameter("latin_keys", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = probes.Select(probe => probe.LatinKey).ToArray() },
            new NpgsqlParameter("shop", NpgsqlDbType.Uuid) { Value = shopId },
            new NpgsqlParameter("supplier", NpgsqlDbType.Uuid) { Value = (object?)supplierId ?? DBNull.Value },
            new NpgsqlParameter("threshold", NpgsqlDbType.Real) { Value = (float)threshold },
            new NpgsqlParameter("max_candidates", NpgsqlDbType.Integer) { Value = IProductSimilarityQueries.MaxCandidatesPerLine },
        ];

        var rows = await db.Database.SqlQueryRaw<SimilarityRow>(Sql, parameters).ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => (row.LineNo, row.ProductId, row.AliasUnitId))
            .Select(group =>
            {
                var first = group.First();
                var units = first.AliasUnitId is null
                    ? group.Where(row => row.UnitId is not null)
                        .Select(row => new NamedProductUnit(row.UnitId!.Value, row.UnitName!, row.UnitIsBase!.Value))
                        .ToList()
                    : [];
                return new SimilarCandidate(first.LineNo, first.ProductId, first.AliasUnitId, first.OtherSupplier, first.Similarity, units);
            })
            .ToList();
    }

    /// <summary>One result row: a candidate, and for a name candidate one of its units.</summary>
    internal sealed class SimilarityRow
    {
        public int LineNo { get; init; }

        public Guid ProductId { get; init; }

        public Guid? AliasUnitId { get; init; }

        public bool OtherSupplier { get; init; }

        public float Similarity { get; init; }

        public Guid? UnitId { get; init; }

        public string? UnitName { get; init; }

        public bool? UnitIsBase { get; init; }
    }
}
