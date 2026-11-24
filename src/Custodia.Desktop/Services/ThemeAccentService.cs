using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace TundraDrive.Desktop.Services;

public static class ThemeAccentService
{
    public const string BlueAccentId = "blue";
    public const string EmeraldAccentId = "emerald";

    public static string NormalizeAccent(string? accent) =>
        string.Equals(accent?.Trim(), EmeraldAccentId, StringComparison.OrdinalIgnoreCase)
            ? EmeraldAccentId
            : BlueAccentId;

    public static void ApplyAccent(string? accent)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyAccent(accent));
            return;
        }

        var isEmerald = NormalizeAccent(accent) == EmeraldAccentId;
        var primary = isEmerald ? "#6DD58C" : "#A8C7FA";
        var primaryHover = isEmerald ? "#84E09F" : "#B9D2FB";
        var primaryPressed = isEmerald ? "#58C278" : "#92B8F7";
        var onPrimary = isEmerald ? "#00391B" : "#062E6F";
        var primaryContainer = isEmerald ? "#00522A" : "#0842A0";
        var onPrimaryContainer = isEmerald ? "#B8F3C8" : "#D3E3FD";
        var secondaryContainer = isEmerald ? "#334940" : "#374955";
        var secondaryContainerHover = isEmerald ? "#3E594E" : "#425665";
        var secondaryContainerPressed = isEmerald ? "#2A3D35" : "#2E3E49";
        var onSecondaryContainer = isEmerald ? "#CBEAD8" : "#D3E4F3";

        SetBrush(app, "M3Primary", primary);
        SetBrush(app, "M3PrimaryHover", primaryHover);
        SetBrush(app, "M3PrimaryPressed", primaryPressed);
        SetBrush(app, "M3OnPrimary", onPrimary);
        SetBrush(app, "M3PrimaryContainer", primaryContainer);
        SetBrush(app, "M3OnPrimaryContainer", onPrimaryContainer);

        SetBrush(app, "M3SecondaryContainer", secondaryContainer);
        SetBrush(app, "M3SecondaryContainerHover", secondaryContainerHover);
        SetBrush(app, "M3SecondaryContainerPressed", secondaryContainerPressed);
        SetBrush(app, "M3OnSecondaryContainer", onSecondaryContainer);

        SetBrush(app, "ComboBoxItemBackgroundSelected", secondaryContainer);
        SetBrush(app, "ComboBoxItemBackgroundSelectedPointerOver", secondaryContainerHover);
        SetBrush(app, "TextControlBorderBrushFocused", primary);
        SetBrush(app, "CheckBoxCheckBackgroundFillChecked", primary);
        SetBrush(app, "CheckBoxCheckBackgroundFillCheckedPointerOver", primaryHover);
        SetBrush(app, "CheckBoxCheckBackgroundFillCheckedPressed", primaryPressed);
        SetBrush(app, "CheckBoxCheckGlyphForegroundChecked", onPrimary);
        SetBrush(app, "CheckBoxCheckGlyphForegroundCheckedPointerOver", onPrimary);
        SetBrush(app, "CheckBoxCheckBackgroundStrokeChecked", primary);
        SetBrush(app, "SliderTrackValueFill", primary);
        SetBrush(app, "SliderTrackValueFillPointerOver", primaryHover);
        SetBrush(app, "SliderThumbBackground", primary);
        SetBrush(app, "SliderThumbBackgroundPointerOver", primaryHover);
        SetBrush(app, "SliderThumbBackgroundPressed", primaryPressed);
    }

    private static void SetBrush(Application app, string key, string hex)
    {
        app.Resources[key] = SolidColorBrush.Parse(hex);
    }
}
