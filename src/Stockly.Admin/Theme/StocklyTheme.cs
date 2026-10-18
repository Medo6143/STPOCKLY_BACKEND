using MudBlazor;

namespace Stockly.Admin.Theme;

/// <summary>The dashboard's MudBlazor theme: teal primary, amber accent, soft surfaces, light and dark palettes.</summary>
public static class StocklyTheme
{
    private static readonly string[] FontFamily = ["Inter", "Segoe UI", "Roboto", "Helvetica Neue", "Arial", "sans-serif"];

    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#0F766E",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#F59E0B",
            Tertiary = "#6366F1",
            Info = "#0284C7",
            Success = "#16A34A",
            Warning = "#D97706",
            Error = "#DC2626",
            Background = "#F4F6FA",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#1F2937",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#374151",
            DrawerIcon = "#64748B",
            TextPrimary = "#1F2937",
            TextSecondary = "#64748B",
            LinesDefault = "#E5E7EB",
            TableLines = "#EEF0F4",
            Divider = "#E5E7EB",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#2DD4BF",
            PrimaryContrastText = "#062925",
            Secondary = "#FBBF24",
            Tertiary = "#818CF8",
            Info = "#38BDF8",
            Success = "#4ADE80",
            Warning = "#FBBF24",
            Error = "#F87171",
            Background = "#0B1117",
            Surface = "#121A22",
            AppbarBackground = "#121A22",
            AppbarText = "#E5E7EB",
            DrawerBackground = "#0F161D",
            DrawerText = "#CBD5E1",
            DrawerIcon = "#94A3B8",
            TextPrimary = "#E5E7EB",
            TextSecondary = "#94A3B8",
            LinesDefault = "#1F2A35",
            TableLines = "#1B2530",
            Divider = "#1F2A35",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = FontFamily },
            H4 = new H4Typography { FontWeight = "700" },
            H5 = new H5Typography { FontWeight = "700" },
            H6 = new H6Typography { FontWeight = "600" },
            Button = new ButtonTypography { FontWeight = "600", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",
            DrawerWidthLeft = "248px",
        },
    };
}
