using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stockly.Domain.Invoices;
using Stockly.Domain.Shops;
using Stockly.Domain.Suppliers;
using Stockly.Infrastructure.Identity;

namespace Stockly.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public const string TableName = "invoices";

    public const string ClientRequestIndex = "ux_invoices_shop_id_client_request_id";

    private static readonly JsonSerializerOptions WarningJson = new()
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable(TableName, table => table.HasCheckConstraint(
            "ck_invoices_confirmed_at", $"confirmed_at IS NULL OR status = {(short)InvoiceStatus.Confirmed}"));

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        // Get-only Domain properties are not mapped by convention; map them explicitly.
        builder.Property(i => i.ShopId);
        builder.Property(i => i.ClientRequestId);
        builder.Property(i => i.CreatedByUserId);
        builder.Property(i => i.CreatedAt);
        builder.Property(i => i.ImageSha256).HasMaxLength(InvoiceLimits.ImageSha256Length).IsFixedLength().IsRequired();
        builder.Property(i => i.ImageObjectKey).IsRequired();
        builder.Property(i => i.Status).HasConversion<short>();
        builder.Property(i => i.TotalAmount).HasPrecision(18, 2);
        builder.Property(i => i.ComputedTotal).HasPrecision(18, 2);
        builder.Property(i => i.FailureReason).HasConversion<string>();
        builder.Property(i => i.RawAiJson).HasColumnType("jsonb");

        // A jsonb array of warning names, e.g. ["TotalMismatch"] (contract §6).
        builder.Property(i => i.Warnings)
            .HasConversion(
                warnings => JsonSerializer.Serialize(warnings, WarningJson),
                json => JsonSerializer.Deserialize<List<InvoiceWarning>>(json, WarningJson) ?? new List<InvoiceWarning>(),
                new ValueComparer<IReadOnlyList<InvoiceWarning>>(
                    (left, right) => left!.SequenceEqual(right!),
                    warnings => warnings.Aggregate(0, (hash, warning) => HashCode.Combine(hash, warning)),
                    warnings => warnings.ToList()))
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property<uint>(ShadowProperties.RowVersion).IsRowVersion();

        builder.HasMany(i => i.Items).WithOne().HasForeignKey(item => item.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Shop>().WithMany().HasForeignKey(i => i.ShopId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(i => i.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.ConfirmedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Scan idempotency (ADR-0006): InvoiceQueries.FindListItemByClientRequestIdAsync; a concurrent repeat loses here.
        builder.HasIndex(i => new { i.ShopId, i.ClientRequestId })
            .IsUnique()
            .HasDatabaseName(ClientRequestIndex);

        // Invoice history (I2, newest first) and the daily quota fallback count (InvoiceRepository.CountCreatedSinceAsync).
        builder.HasIndex(i => new { i.ShopId, i.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_invoices_shop_id_created_at");

        // I2 with a status filter, newest first; "waiting for review" badge (backend.md §17.1 item 4).
        builder.HasIndex(i => new { i.ShopId, i.Status, i.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_invoices_shop_id_status_created_at");

        // Duplicate photo: InvoiceRepository.FindReusableExtractionAsync (DB side of the AI cache).
        builder.HasIndex(i => new { i.ShopId, i.ImageSha256 })
            .HasDatabaseName("ix_invoices_shop_id_image_sha256");

        // DuplicateInvoiceNumberRule: InvoiceRepository.ExistsOtherWithNumberAsync.
        builder.HasIndex(i => new { i.ShopId, i.SupplierId, i.InvoiceNumber })
            .HasFilter("invoice_number IS NOT NULL")
            .HasDatabaseName("ix_invoices_shop_id_supplier_id_invoice_number");

        // Admin "failed scans" screen.
        builder.HasIndex(i => new { i.Status, i.CreatedAt })
            .IsDescending(false, true)
            .HasFilter($"status = {(short)InvoiceStatus.Failed}")
            .HasDatabaseName("ix_invoices_status_created_at_failed");
    }
}
