using MudBlazor;

namespace Stockly.Admin.Components;

/// <summary>Opens the admin dialogs and returns what they produced, or <c>null</c> when the admin cancelled.</summary>
public static class DialogExtensions
{
    private static readonly DialogOptions Options = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseButton = true,
        BackdropClick = false,
    };

    /// <summary>Asks for the reason of an audited write; <c>null</c> when cancelled.</summary>
    public static Task<string?> AskReasonAsync(
        this IDialogService dialogs, string title, string message, string confirmText, Color confirmColor)
    {
        var parameters = new DialogParameters<ReasonDialog>
        {
            { dialog => dialog.Title, title },
            { dialog => dialog.Message, message },
            { dialog => dialog.ConfirmText, confirmText },
            { dialog => dialog.ConfirmColor, confirmColor },
        };
        return ShowAsync<ReasonDialog, string>(dialogs, title, parameters);
    }

    /// <summary>Shows <typeparamref name="TDialog"/> and returns its result of type <typeparamref name="TResult"/>.</summary>
    public static async Task<TResult?> ShowAsync<TDialog, TResult>(
        this IDialogService dialogs, string title, DialogParameters<TDialog> parameters)
        where TDialog : Microsoft.AspNetCore.Components.IComponent
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(dialogs);

        var reference = await dialogs.ShowAsync<TDialog>(title, parameters, Options);
        var result = await reference.Result;
        return result is { Canceled: false, Data: TResult value } ? value : null;
    }
}
