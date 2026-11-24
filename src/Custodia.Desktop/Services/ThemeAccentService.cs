using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace TundraDrive.Desktop.Services;

public static class ThemeAccentService
{
    public const string BlueAccentId = "blue";
    public const string EmeraldAccentId = "emerald";
    public const string PurpleAccentId = "purple";
    public const string AmberAccentId = "amber";
    public const string RoseAccentId = "rose";
    public const string CyanAccentId = "cyan";

    private sealed record AccentPalette(
        string Id,
        string Primary,
        string PrimaryHover,
        string PrimaryPressed,
        string OnPrimary,
        string PrimaryContainer,
        string OnPrimaryContainer,
        string SecondaryContainer,
        string SecondaryContainerHover,
        string SecondaryContainerPressed,
        string OnSecondaryContainer);

    private static readonly AccentPalette[] Palettes =
    [
        new(
            BlueAccentId,
            Primary: "#A8C7FA",
            PrimaryHover: "#B9D2FB",
            PrimaryPressed: "#92B8F7",
            OnPrimary: "#062E6F",
            PrimaryContainer: "#0842A0",
            OnPrimaryContainer: "#D3E3FD",
            SecondaryContainer: "#374955",
            SecondaryContainerHover: "#425665",
            SecondaryContainerPressed: "#2E3E49",
            OnSecondaryContainer: "#D3E4F3"),
        new(
            EmeraldAccentId,
            Primary: "#6DD58C",
            PrimaryHover: "#84E09F",
            PrimaryPressed: "#58C278",
            OnPrimary: "#00391B",
            PrimaryContainer: "#00522A",
            OnPrimaryContainer: "#B8F3C8",
            SecondaryContainer: "#334940",
            SecondaryContainerHover: "#3E594E",
            SecondaryContainerPressed: "#2A3D35",
            OnSecondaryContainer: "#CBEAD8"),
        new(
            PurpleAccentId,
            Primary: "#D0BCFF",
            PrimaryHover: "#DCCEFF",
            PrimaryPressed: "#BFA6FA",
            OnPrimary: "#381E72",
            PrimaryContainer: "#4F378B",
            OnPrimaryContainer: "#EADDFF",
            SecondaryContainer: "#4A4458",
            SecondaryContainerHover: "#585168",
            SecondaryContainerPressed: "#3D3849",
            OnSecondaryContainer: "#E8DEF8"),
        new(
            AmberAccentId,
            Primary: "#F5C26B",
            PrimaryHover: "#F8CE85",
            PrimaryPressed: "#E9B254",
            OnPrimary: "#452B00",
            PrimaryContainer: "#633F00",
            OnPrimaryContainer: "#FFDEA8",
            SecondaryContainer: "#4D412F",
            SecondaryContainerHover: "#5C4E3A",
            SecondaryContainerPressed: "#3F3526",
            OnSecondaryContainer: "#F5DFC2"),
        new(
            RoseAccentId,
            Primary: "#F2B8B5",
            PrimaryHover: "#F6C8C6",
            PrimaryPressed: "#E8A29E",
            OnPrimary: "#601410",
            PrimaryContainer: "#8C1D18",
            OnPrimaryContainer: "#F9DEDC",
            SecondaryContainer: "#533C3D",
            SecondaryContainerHover: "#63494A",
            SecondaryContainerPressed: "#443132",
            OnSecondaryContainer: "#F4D9DA"),
        new(
            CyanAccentId,
            Primary: "#7CD4E6",
            PrimaryHover: "#95DFF0",
            PrimaryPressed: "#63C4D8",
            OnPrimary: "#003640",
            PrimaryContainer: "#004E5C",
            OnPrimaryContainer: "#B8EEFA",
            SecondaryContainer: "#314950",
            SecondaryContainerHover: "#3C5860",
            SecondaryContainerPressed: "#283B41",
            OnSecondaryContainer: "#CBE8F0")
    ];

    public static string NormalizeAccent(string? accent)
    {
        var normalized = accent?.Trim();
        foreach (var palette in Palettes)
        {
            if (string.Equals(normalized, palette.Id, StringComparison.OrdinalIgnoreCase))
            {
                return palette.Id;
            }
        }

        return BlueAccentId;
    }

    public static int GetAccentIndex(string? accent)
    {
        var normalized = NormalizeAccent(accent);
        for (var i = 0; i < Palettes.Length; i++)
        {
            if (Palettes[i].Id == normalized)
            {
                return i;
            }
        }

        return 0;
    }

    public static string GetAccentIdByIndex(int index) =>
        index >= 0 && index < Palettes.Length ? Palettes[index].Id : BlueAccentId;

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

        var palette = Palettes[GetAccentIndex(accent)];

        SetBrush(app, "M3Primary", palette.Primary);
        SetBrush(app, "M3PrimaryHover", palette.PrimaryHover);
        SetBrush(app, "M3PrimaryPressed", palette.PrimaryPressed);
        SetBrush(app, "M3OnPrimary", palette.OnPrimary);
        SetBrush(app, "M3PrimaryContainer", palette.PrimaryContainer);
        SetBrush(app, "M3OnPrimaryContainer", palette.OnPrimaryContainer);

        SetBrush(app, "M3SecondaryContainer", palette.SecondaryContainer);
        SetBrush(app, "M3SecondaryContainerHover", palette.SecondaryContainerHover);
        SetBrush(app, "M3SecondaryContainerPressed", palette.SecondaryContainerPressed);
        SetBrush(app, "M3OnSecondaryContainer", palette.OnSecondaryContainer);

        SetBrush(app, "ComboBoxItemBackgroundSelected", palette.SecondaryContainer);
        SetBrush(app, "ComboBoxItemBackgroundSelectedPointerOver", palette.SecondaryContainerHover);
        SetBrush(app, "TextControlBorderBrushFocused", palette.Primary);
        SetBrush(app, "CheckBoxCheckBackgroundFillChecked", palette.Primary);
        SetBrush(app, "CheckBoxCheckBackgroundFillCheckedPointerOver", palette.PrimaryHover);
        SetBrush(app, "CheckBoxCheckBackgroundFillCheckedPressed", palette.PrimaryPressed);
        SetBrush(app, "CheckBoxCheckGlyphForegroundChecked", palette.OnPrimary);
        SetBrush(app, "CheckBoxCheckGlyphForegroundCheckedPointerOver", palette.OnPrimary);
        SetBrush(app, "CheckBoxCheckBackgroundStrokeChecked", palette.Primary);
        SetBrush(app, "SliderTrackValueFill", palette.Primary);
        SetBrush(app, "SliderTrackValueFillPointerOver", palette.PrimaryHover);
        SetBrush(app, "SliderThumbBackground", palette.Primary);
        SetBrush(app, "SliderThumbBackgroundPointerOver", palette.PrimaryHover);
        SetBrush(app, "SliderThumbBackgroundPressed", palette.PrimaryPressed);
    }

    private static void SetBrush(Application app, string key, string hex)
    {
        app.Resources[key] = SolidColorBrush.Parse(hex);
    }
}
