namespace Stockly.Admin.Theme;

/// <summary>Light or dark mode for the whole app. Starts from the system preference; the app bar toggle switches it.</summary>
public sealed class ThemeState
{
    public event EventHandler? Changed;

    public bool IsDarkMode { get; private set; }

    /// <summary>True once the system preference was read; the toggle wins afterwards.</summary>
    public bool IsInitialized { get; private set; }

    public void Initialize(bool systemPrefersDark)
    {
        if (IsInitialized)
        {
            return;
        }

        IsInitialized = true;
        Set(systemPrefersDark);
    }

    public void Toggle() => Set(!IsDarkMode);

    private void Set(bool isDarkMode)
    {
        IsDarkMode = isDarkMode;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
