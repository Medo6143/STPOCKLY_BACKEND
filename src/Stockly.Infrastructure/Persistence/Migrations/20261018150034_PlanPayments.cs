using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockly.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Plan payments through Paymob (ADR-0015, contract §12): the <c>payments</c> table with its idempotency, callback and
    /// listing indexes (docs/erd.md §3.8).
    /// </summary>
    public partial class PlanPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    method = table.Column<short>(type: "smallint", nullable: false),
                    provider = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    provider_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    checkout_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    reference_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.CheckConstraint("ck_payments_paid_at", "(status = 2) = (paid_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_payments_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_created_at",
                table: "payments",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_payments_expires_at_pending",
                table: "payments",
                column: "expires_at",
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_payments_paid_at_paid",
                table: "payments",
                column: "paid_at",
                filter: "status = 2");

            migrationBuilder.CreateIndex(
                name: "ix_payments_plan_id",
                table: "payments",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_shop_id_created_at",
                table: "payments",
                columns: new[] { "shop_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_payments_shop_id_created_at_pending",
                table: "payments",
                columns: new[] { "shop_id", "created_at" },
                descending: new[] { false, true },
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_payments_status_created_at",
                table: "payments",
                columns: new[] { "status", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_payments_provider_provider_order_id",
                table: "payments",
                columns: new[] { "provider", "provider_order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payments_provider_transaction_id",
                table: "payments",
                column: "provider_transaction_id",
                unique: true,
                filter: "provider_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payments_shop_id_client_request_id",
                table: "payments",
                columns: new[] { "shop_id", "client_request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments");
        }
    }
}
