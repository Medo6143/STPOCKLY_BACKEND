using Stockly.Domain.Platform;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Platform;

public sealed class AdminAuditLogTests
{
    [Fact]
    public void Create_ValidEntry_KeepsEveryFieldAndTrimsTheReason()
    {
        var target = Guid.CreateVersion7();

        var entry = AdminAuditLog.Create(UserId, "Shop.Suspend", "Shop", target, "  unpaid ", """{"status":"Active"}""", """{"status":"Suspended"}""", Now);

        entry.Id.Should().NotBe(Guid.Empty);
        entry.AdminUserId.Should().Be(UserId);
        entry.Action.Should().Be("Shop.Suspend");
        entry.TargetType.Should().Be("Shop");
        entry.TargetId.Should().Be(target);
        entry.Reason.Should().Be("unpaid");
        entry.Before.Should().Be("""{"status":"Active"}""");
        entry.After.Should().Be("""{"status":"Suspended"}""");
        entry.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_BlankReason_StoresNull()
    {
        AdminAuditLog.Create(UserId, "Plan.Create", "Plan", Guid.CreateVersion7(), "  ", null, "{}", Now).Reason.Should().BeNull();
    }

    [Fact]
    public void Create_MissingAdminTargetOrAction_Throws()
    {
        var target = Guid.CreateVersion7();

        FluentActions.Invoking(() => AdminAuditLog.Create(Guid.Empty, "a", "b", target, null, null, null, Now)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => AdminAuditLog.Create(UserId, "a", "b", Guid.Empty, null, null, null, Now)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => AdminAuditLog.Create(UserId, " ", "b", target, null, null, null, Now)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => AdminAuditLog.Create(UserId, new string('a', 65), "b", target, null, null, null, Now))
            .Should().Throw<ArgumentException>();
    }
}
