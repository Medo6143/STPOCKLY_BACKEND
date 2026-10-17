using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlatformAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "shops",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Existing shops: the Domain normalizer (ArabicTextNormalizer) in SQL. Tashkeel, superscript alef and tatweel
            // removed; alef forms, teh marbuta, alef maksura and Arabic-Indic/Persian digits mapped; lower case; spaces
            // collapsed and trimmed. New and renamed shops get the value from the Domain.
            migrationBuilder.Sql(
                """
                UPDATE shops SET normalized_name = btrim(regexp_replace(lower(translate(
                    regexp_replace(name, '[\u064B-\u0652\u0670\u0640]', '', 'g'),
                    'أإآٱةى٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹',
                    'ااااهي01234567890123456789')), '\s+', ' ', 'g'));
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suspended_at",
                table: "shops",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspended_reason",
                table: "shops",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "daily_scan_quota_override",
                table: "shop_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "admin_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_admin_audit_logs_users_admin_user_id",
                        column: x => x.admin_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email_pattern",
                table: "users",
                column: "normalized_email")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_shops_created_at",
                table: "shops",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_shops_normalized_name_trgm",
                table: "shops",
                column: "normalized_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_shop_subscriptions_daily_scan_quota_override",
                table: "shop_subscriptions",
                sql: "daily_scan_quota_override IS NULL OR daily_scan_quota_override BETWEEN 0 AND 10000");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_logs_admin_user_id_created_at",
                table: "admin_audit_logs",
                columns: new[] { "admin_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_logs_created_at",
                table: "admin_audit_logs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_logs_target_type_target_id_created_at",
                table: "admin_audit_logs",
                columns: new[] { "target_type", "target_id", "created_at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_users_normalized_email_pattern",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_shops_created_at",
                table: "shops");

            migrationBuilder.DropIndex(
                name: "ix_shops_normalized_name_trgm",
                table: "shops");

            migrationBuilder.DropCheckConstraint(
                name: "ck_shop_subscriptions_daily_scan_quota_override",
                table: "shop_subscriptions");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "suspended_at",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "suspended_reason",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "daily_scan_quota_override",
                table: "shop_subscriptions");
        }
    }
}
