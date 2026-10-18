using System;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockly.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Sprint 6: cross-script fuzzy matching and the image lifecycle (docs/erd.md §4). <c>latin_key</c> on products and
    /// aliases is backfilled by <c>stockly_latin_key(text)</c>, a SQL copy of <c>Domain/Text/LatinKey</c> applied to the
    /// already-normalized column (a test keeps both in step); the function stays for future re-backfills. The column
    /// default is dropped after the backfill, so every write must set the key (the application always does).
    /// </summary>
    public partial class MatchingAndImageLifecycle : Migration
    {
        // LatinKey step 3 as translate(): the mapped letters, then the dropped ones (no counterpart in To).
        private const string TranslateFrom =
            "bcdfghjklmnpqrstvz"
            + "بتثجحخدذرزسشصضطظ"
            + "غفقكکلمنهةپچڤگ"
            + "aeiouwy"
            + "اأإآٱءؤئوىيیع";

        private const string TranslateTo =
            "bkdfghgklmnbkrstfz"
            + "bttghkdzrzsssdtz"
            + "gfkkklmnhhbsfg";

        // LatinKey steps 2 to 7 in the same order; {0} and {1} are the translate() lists above.
        private const string LatinKeyFunction = """
            CREATE OR REPLACE FUNCTION stockly_latin_key(normalized text) RETURNS text
            LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS $fn$
            SELECT btrim(regexp_replace(
                regexp_replace(
                    regexp_replace(
                        regexp_replace(
                            translate(
                                regexp_replace(
                                    replace(replace(replace(replace(replace(replace(replace(replace(normalized,
                                        'ph', 'f'), 'sh', 's'), 'ch', 's'), 'th', 't'), 'kh', 'k'), 'gh', 'g'), 'ck', 'k'), 'x', 'ks'),
                                    'c([eiy])', 's\1', 'g'),
                                '{0}', '{1}'),
                            '[^a-z0-9]+', ' ', 'g'),
                        '([a-z])\1+', '\1', 'g'),
                    'h( |$)', '\1', 'g'),
                ' +', ' ', 'g'))
            $fn$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "latin_key",
                table: "products",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "latin_key",
                table: "product_aliases",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "image_deleted_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(string.Format(CultureInfo.InvariantCulture, LatinKeyFunction, TranslateFrom, TranslateTo));
            migrationBuilder.Sql("UPDATE products SET latin_key = stockly_latin_key(normalized_name);");
            migrationBuilder.Sql("UPDATE product_aliases SET latin_key = stockly_latin_key(normalized_alias);");
            migrationBuilder.Sql("ALTER TABLE products ALTER COLUMN latin_key DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE product_aliases ALTER COLUMN latin_key DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_products_latin_key_trgm",
                table: "products",
                column: "latin_key")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_product_aliases_latin_key_trgm",
                table: "product_aliases",
                column: "latin_key")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_created_at_image_kept",
                table: "invoices",
                column: "created_at",
                filter: "image_deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropIndex(
                name: "ix_products_latin_key_trgm",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_product_aliases_latin_key_trgm",
                table: "product_aliases");

            migrationBuilder.DropIndex(
                name: "ix_invoices_created_at_image_kept",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "latin_key",
                table: "products");

            migrationBuilder.DropColumn(
                name: "latin_key",
                table: "product_aliases");

            migrationBuilder.DropColumn(
                name: "image_deleted_at",
                table: "invoices");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS stockly_latin_key(text);");
        }
    }
}
