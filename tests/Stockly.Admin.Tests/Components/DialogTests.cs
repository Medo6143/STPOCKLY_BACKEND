using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Stockly.Admin.Components;
using Stockly.Admin.Tests.Support;
using Stockly.Contracts.Admin;

namespace Stockly.Admin.Tests.Components;

public sealed class DialogTests : AdminTestContext
{
    [Fact]
    public async Task ReasonDialog_BlankReason_ConfirmDisabled()
    {
        var dialog = await ShowReasonDialogAsync();

        Dialogs.Find("[data-testid=reason-confirm]").HasAttribute("disabled").Should().BeTrue();
        Dialogs.Find("textarea").Input(" \t ");
        Dialogs.Find("[data-testid=reason-confirm]").HasAttribute("disabled").Should().BeTrue();
        dialog.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task ReasonDialog_WithReason_ReturnsTheTrimmedReason()
    {
        var dialog = await ShowReasonDialogAsync();

        Dialogs.Find("textarea").Input("  Unpaid invoice  ");
        Dialogs.Find("[data-testid=reason-confirm]").Click();

        (await dialog).Should().Be("Unpaid invoice");
    }

    [Fact]
    public async Task ReasonDialog_Cancel_ReturnsNull()
    {
        var dialog = await ShowReasonDialogAsync();

        Dialogs.Find("[data-testid=reason-cancel]").Click();

        (await dialog).Should().BeNull();
    }

    [Fact]
    public async Task QuotaOverrideDialog_BlankReason_ConfirmDisabled_ThenRemovesOverride()
    {
        var parameters = new DialogParameters<QuotaOverrideDialog>
        {
            { d => d.PlanQuota, 20 },
            { d => d.CurrentOverride, 50 },
        };
        var service = Services.GetRequiredService<IDialogService>();
        var dialog = Dialogs.InvokeAsync(() => service.ShowAsync<QuotaOverrideDialog, QuotaOverrideRequest>("Quota", parameters));

        Dialogs.WaitForElement("[data-testid=reason-confirm]").HasAttribute("disabled").Should().BeTrue();
        Dialogs.Find("input[type=checkbox]").Change(false);
        Dialogs.Find("textarea").Input("Back to plan");
        Dialogs.Find("[data-testid=reason-confirm]").Click();

        (await dialog).Should().Be(new QuotaOverrideRequest(null, "Back to plan"));
    }

    private async Task<Task<string?>> ShowReasonDialogAsync()
    {
        var service = Services.GetRequiredService<IDialogService>();
        var dialog = Dialogs.InvokeAsync(() => service.AskReasonAsync("Suspend shop", "Members are signed out.", "Suspend", Color.Error));
        Dialogs.WaitForElement("textarea");
        await Task.Yield();
        return dialog;
    }
}
