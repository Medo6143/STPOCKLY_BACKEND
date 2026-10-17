using NSubstitute;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Admin.CreateHangfireSession;
using Stockly.Application.Features.Admin.GetAdminInvoice;
using Stockly.Application.Features.Admin.GetAuditLogs;
using Stockly.Application.Features.Admin.GetFailedInvoices;
using Stockly.Application.Features.Admin.RerunInvoice;
using Stockly.Application.Features.Admin.SeedPlatformAdmin;
using Stockly.Application.Features.Auth;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Invoices;
using static Stockly.Application.Tests.Support.AdminTestContext;
using ContractInvoiceStatus = Stockly.Contracts.Invoices.InvoiceStatus;
using DomainInvoiceStatus = Stockly.Domain.Invoices.InvoiceStatus;

namespace Stockly.Application.Tests.Features.Admin;

/// <summary>AD17–AD20, the Hangfire session and the start-up admin seed.</summary>
public sealed class AdminInvoiceAndMiscHandlerTests
{
    private static readonly Guid ShopId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000b1");

    private readonly AdminTestContext _context = new();

    [Fact]
    public async Task Rerun_FailedInvoice_ProcessingAuditedThenQueuedAfterCommitWithStatusCache()
    {
        var invoice = FailedInvoice();
        _context.Invoices.FindForUpdateAsync(invoice.Id, Arg.Any<CancellationToken>()).Returns(invoice);
        var record = Record(invoice.Id, ContractInvoiceStatus.Processing);
        _context.Invoices.FindAsync(invoice.Id, Arg.Any<CancellationToken>()).Returns(record);
        var commitsWhenQueued = -1;
        _context.Jobs.When(jobs => jobs.EnqueueRerunInvoice(ShopId, invoice.Id)).Do(_ => commitsWhenQueued = _context.UnitOfWork.CommitCount);

        var result = await Rerun(invoice.Id, "AI outage over");

        result.Value.Should().Be(record.Invoice);
        invoice.Status.Should().Be(DomainInvoiceStatus.Processing);
        commitsWhenQueued.Should().Be(1, "the job is queued only after the transaction committed");
        _context.Jobs.DidNotReceiveWithAnyArgs().EnqueueProcessInvoice(default, default);
        await _context.StatusCache.Received(1).SetAsync(
            ShopId, Arg.Is<InvoiceStatusSnapshot>(snapshot => snapshot.Status == DomainInvoiceStatus.Processing), Arg.Any<CancellationToken>());
        var entry = _context.Audited.Should().ContainSingle().Subject;
        entry.Should().BeEquivalentTo(new { Action = AdminActions.InvoiceRerun, TargetType = AdminTargets.Invoice, TargetId = invoice.Id, Reason = "AI outage over" });
        entry.Before.Should().Be("""{"status":"Failed","failureReason":"AiUnavailable"}""");
        entry.After.Should().Be("""{"status":"Processing"}""");
    }

    [Fact]
    public async Task Rerun_NotFailed_IsNotRerunnableAndNothingQueued()
    {
        var invoice = Invoice.CreateForScan(ShopId, Guid.CreateVersion7(), AdminId, new string('a', 64), Now, id => $"k/{id}").Value;
        _context.Invoices.FindForUpdateAsync(invoice.Id, Arg.Any<CancellationToken>()).Returns(invoice);

        (await Rerun(invoice.Id, "x")).Error.Should().Be(InvoiceErrors.NotRerunnable);

        _context.UnitOfWork.CommitCount.Should().Be(0);
        _context.Jobs.DidNotReceiveWithAnyArgs().EnqueueRerunInvoice(default, default);
        _context.Audited.Should().BeEmpty();
    }

    [Fact]
    public async Task Rerun_UnknownInvoice_IsNotFound_BlankReason_IsReasonRequired()
    {
        (await Rerun(Guid.CreateVersion7(), "x")).Error.Should().Be(InvoiceErrors.NotFound);
        (await Rerun(Guid.CreateVersion7(), " ")).Error.Should().Be(AdminErrors.ReasonRequired);
        _context.Jobs.DidNotReceiveWithAnyArgs().EnqueueRerunInvoice(default, default);
    }

    [Fact]
    public void RerunValidator_ReasonLength()
    {
        new RerunInvoiceCommandValidator().Validate(new RerunInvoiceCommand(Guid.CreateVersion7(), new string('x', 501))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task GetAdminInvoice_SignsTheImageForFiveMinutes()
    {
        var id = Guid.CreateVersion7();
        var record = Record(id, ContractInvoiceStatus.Failed);
        _context.Invoices.FindAsync(id, Arg.Any<CancellationToken>()).Returns(record);
        _context.Storage.GetDownloadUrlAsync("invoices/key.jpg", TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>()).Returns("http://minio/signed");

        var result = await new GetAdminInvoiceQueryHandler(_context.Invoices, _context.Storage)
            .Handle(new GetAdminInvoiceQuery(id), CancellationToken.None);

        result.Value.Should().BeEquivalentTo(new { ShopId, ShopName = "صيدلية", ImageUrl = "http://minio/signed", RawAiJson = "{\"items\":[]}" });
    }

    [Fact]
    public async Task GetAdminInvoice_ImageGone_NullUrl_Unknown_NotFound()
    {
        var id = Guid.CreateVersion7();
        _context.Invoices.FindAsync(id, Arg.Any<CancellationToken>()).Returns(Record(id, ContractInvoiceStatus.Failed));
        _context.Storage.GetDownloadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        var handler = new GetAdminInvoiceQueryHandler(_context.Invoices, _context.Storage);

        (await handler.Handle(new GetAdminInvoiceQuery(id), CancellationToken.None)).Value.ImageUrl.Should().BeNull();
        (await handler.Handle(new GetAdminInvoiceQuery(Guid.CreateVersion7()), CancellationToken.None)).Error.Should().Be(InvoiceErrors.NotFound);
    }

    [Fact]
    public async Task GetFailedInvoices_AndAuditLogs_PassFiltersThrough()
    {
        var failed = new PagedResponse<AdminFailedInvoice>([], 1, 20, 0);
        _context.Invoices.ListFailedAsync(ShopId, 1, 20, Arg.Any<CancellationToken>()).Returns(failed);
        var logs = new PagedResponse<AuditLogEntry>([], 2, 5, 0);
        _context.AuditLog.ListAsync("Shop", ShopId, AdminId, 2, 5, Arg.Any<CancellationToken>()).Returns(logs);

        (await new GetFailedInvoicesQueryHandler(_context.Invoices).Handle(new GetFailedInvoicesQuery(ShopId, 1, 20), CancellationToken.None))
            .Value.Should().BeSameAs(failed);
        (await new GetAuditLogsQueryHandler(_context.AuditLog).Handle(new GetAuditLogsQuery(" Shop ", ShopId, AdminId, 2, 5), CancellationToken.None))
            .Value.Should().BeSameAs(logs);
    }

    [Fact]
    public void ListValidators_Paging()
    {
        new GetFailedInvoicesQueryValidator().Validate(new GetFailedInvoicesQuery(null, 1, 101)).IsValid.Should().BeFalse();
        new GetAuditLogsQueryValidator().Validate(new GetAuditLogsQuery(null, null, null, 0, 20)).IsValid.Should().BeFalse();
        new GetAuditLogsQueryValidator().Validate(new GetAuditLogsQuery(new string('x', 65), null, null, 1, 20)).IsValid.Should().BeFalse();
        new GetAuditLogsQueryValidator().Validate(new GetAuditLogsQuery("Shop", null, null, 1, 100)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task HangfireSession_IssuesACodeForTheAdmin()
    {
        _context.Codes.Issue(AdminId).Returns("abc+/=");

        var result = await new CreateHangfireSessionCommandHandler(_context.CurrentUser, _context.Codes)
            .Handle(new CreateHangfireSessionCommand(), CancellationToken.None);

        result.Value.Url.Should().Be("/hangfire?code=abc%2B%2F%3D");
    }

    [Fact]
    public async Task HangfireSession_NoUser_IsUnauthenticated()
    {
        _context.CurrentUser.UserId.Returns((Guid?)null);

        var result = await new CreateHangfireSessionCommandHandler(_context.CurrentUser, _context.Codes)
            .Handle(new CreateHangfireSessionCommand(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task Seed_NoAdmin_CreatesOne()
    {
        var created = Guid.CreateVersion7();
        _context.Admins.CreateAsync(Arg.Any<NewUserAccount>(), "Str0ngPass", Arg.Any<CancellationToken>()).Returns(created);

        var result = await Seed();

        result.Value.Should().Be(new SeedPlatformAdminOutcome(true, created));
        await _context.Admins.Received(1).CreateAsync(
            Arg.Is<NewUserAccount>(account => account.Email == "admin@example.com" && account.FullName == "Platform Admin" && account.CreatedAt == Now),
            "Str0ngPass",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seed_AdminExists_CreatesNothing()
    {
        _context.Admins.AnyAsync(Arg.Any<CancellationToken>()).Returns(true);

        (await Seed()).Value.Should().Be(SeedPlatformAdminOutcome.AlreadyExists);
        await _context.Admins.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Seed_EmailOfAnExistingUser_IsEmailTaken()
    {
        _context.Admins.CreateAsync(Arg.Any<NewUserAccount>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(AuthErrors.EmailTaken);

        (await Seed()).Error.Should().Be(AuthErrors.EmailTaken);
    }

    [Fact]
    public void SeedValidator_PasswordPolicyAndEmail()
    {
        var validator = new SeedPlatformAdminCommandValidator();

        validator.Validate(new SeedPlatformAdminCommand("admin@example.com", "Str0ngPass", "Admin")).IsValid.Should().BeTrue();
        validator.Validate(new SeedPlatformAdminCommand("not-an-email", "short", "")).Errors.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void SeedOptions_IsComplete_OnlyWithEveryValue_AndNeverPrintsThePassword()
    {
        var options = new AdminSeedOptions { Email = "a@b.c", Password = "Secret123", FullName = "A" };

        options.IsComplete.Should().BeTrue();
        options.ToString().Should().NotContain("Secret123");
        new AdminSeedOptions { Email = "a@b.c", FullName = "A" }.IsComplete.Should().BeFalse();
    }

    private Task<Domain.Common.Result<SeedPlatformAdminOutcome>> Seed() =>
        new SeedPlatformAdminCommandHandler(_context.Admins, _context.Clock)
            .Handle(new SeedPlatformAdminCommand(" admin@example.com ", "Str0ngPass", " Platform Admin "), CancellationToken.None);

    private Task<Domain.Common.Result<InvoiceDetail>> Rerun(Guid id, string? reason) =>
        new RerunInvoiceCommandHandler(_context.CurrentUser, _context.Invoices, _context.Audit, _context.Jobs, _context.StatusCache, _context.UnitOfWork)
            .Handle(new RerunInvoiceCommand(id, reason), CancellationToken.None);

    private static Invoice FailedInvoice()
    {
        var invoice = Invoice.CreateForScan(ShopId, Guid.CreateVersion7(), AdminId, new string('a', 64), Now.AddHours(-1), id => $"k/{id}").Value;
        invoice.MarkFailed(InvoiceFailureReason.AiUnavailable, null, Now.AddMinutes(-50));
        return invoice;
    }

    private static AdminInvoiceRecord Record(Guid id, ContractInvoiceStatus status) =>
        new(
            new InvoiceDetail(id, Guid.CreateVersion7(), status, null, null, null, null, null, null, [], null, [], Now.UtcDateTime, null, null),
            ShopId,
            "صيدلية",
            "invoices/key.jpg",
            "{\"items\":[]}",
            []);
}
