using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;

namespace TundraDrive.Desktop.Services;

public static class ThemeAccentService
{
    private static readonly ConcurrentDictionary<string, IBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    public const string DarkThemeId = "dark";
    public const string LightThemeId = "light";
    public const string SystemThemeId = "system";

    public const string BlueAccentId = "blue";
    public const string EmeraldAccentId = "emerald";
    public const string PurpleAccentId = "purple";
    public const string AmberAccentId = "amber";
    public const string RoseAccentId = "rose";
    public const string CyanAccentId = "cyan";

    private static readonly string[] ThemeOrder = [DarkThemeId, LightThemeId, SystemThemeId];

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

    private static readonly AccentPalette[] DarkPalettes =
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

    private static readonly AccentPalette[] LightPalettes =
    [
        new(
            BlueAccentId,
            Primary: "#0B57D0",
            PrimaryHover: "#094BB5",
            PrimaryPressed: "#0842A0",
            OnPrimary: "#FFFFFF",
            PrimaryContainer: "#D3E3FD",
            OnPrimaryContainer: "#041E49",
            SecondaryContainer: "#D8E6F8",
            SecondaryContainerHover: "#C9DCF4",
            SecondaryContainerPressed: "#BAD1EF",
            OnSecondaryContainer: "#0D2948"),
        new(
            EmeraldAccentId,
            Primary: "#146C2E",
            PrimaryHover: "#105B26",
            PrimaryPressed: "#0C491E",
            OnPrimary: "#FFFFFF",
            PrimaryContainer: "#C4EED0",
            OnPrimaryContainer: "#072711",
            SecondaryContainer: "#D3EBD9",
            SecondaryContainerHover: "#C3E2CB",
            SecondaryContainerPressed: "#B4D8BE",
            OnSecondaryContainer: "#0C2E18"),
        new(
            PurpleAccentId,
            Primary: "#6750A4",
            PrimaryHover: "#5A4494",
            PrimaryPressed: "#4F378B",
            OnPrimary: "#FFFFFF",
            PrimaryContainer: "#EADDFF",
            OnPrimaryContainer: "#21005D",
            SecondaryContainer: "#E6DDF7",
            SecondaryContainerHover: "#DACEF2",
            SecondaryContainerPressed: "#CFC0EC",
            OnSecondaryContainer: "#23183C"),
        new(
            AmberAccentId,
            Primary: "#9A5B00",
            PrimaryHover: "#854E00",
            PrimaryPressed: "#704100",
            OnPrimary: "#FFFFFF",
            PrimaryContainer: "#FFDEA8",
            OnPrimaryContainer: "#2B1700",
            SecondaryContainer: "#F6E4C5",
            SecondaryContainerHover: "#EED8B2",
            SecondaryContainerPressed: "#E5CC9F",
            OnSecondaryContainer: "#2F1D00"),
        new(
            RoseAccentId,
            Primary: "#B32645",
            PrimaryHover: "#9D203C",
            PrimaryPressed: "#861932",
            OnPrimary: "#FFFFFF",
            PrimaryContainer: "#FFD9DF",
            OnPrimaryContainer: "#3F0013",
            SecondaryContainer: "#F8DCE1",
            SecondaryContainerHover: "#F2CCD3",
            SecondaryContainerPressed: "#EBBCC5",
            OnSecondaryContainer: "#3B0917"),
        new(
            CyanAccentId,
            Primary: "#006879",
            PrimaryHover: "#005867",
            PrimaryPressed: "#004956",
            OnPrimary: "#FFFFFF",
            PrimaryContainer: "#B4EEFA",
            OnPrimaryContainer: "#001F25",
            SecondaryContainer: "#CCEAF0",
            SecondaryContainerHover: "#BCE1E8",
            SecondaryContainerPressed: "#ACD7E0",
            OnSecondaryContainer: "#002730")
    ];

    private static string _currentThemeMode = DarkThemeId;
    private static string _currentAccentId = BlueAccentId;
    private static bool _actualThemeHooked;
    private static bool _applying;

    public static bool IsLightTheme { get; private set; }
    public static event EventHandler? ThemeChanged;

    public static string NormalizeTheme(string? themeMode)
    {
        var normalized = themeMode?.Trim();
        foreach (var id in ThemeOrder)
        {
            if (string.Equals(normalized, id, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return DarkThemeId;
    }

    public static int GetThemeIndex(string? themeMode)
    {
        var normalized = NormalizeTheme(themeMode);
        for (var i = 0; i < ThemeOrder.Length; i++)
        {
            if (ThemeOrder[i] == normalized)
            {
                return i;
            }
        }

        return 0;
    }

    public static string GetThemeIdByIndex(int index) =>
        index >= 0 && index < ThemeOrder.Length ? ThemeOrder[index] : DarkThemeId;

    public static string NormalizeAccent(string? accent)
    {
        var normalized = accent?.Trim();
        foreach (var palette in DarkPalettes)
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
        for (var i = 0; i < DarkPalettes.Length; i++)
        {
            if (DarkPalettes[i].Id == normalized)
            {
                return i;
            }
        }

        return 0;
    }

    public static string GetAccentIdByIndex(int index) =>
        index >= 0 && index < DarkPalettes.Length ? DarkPalettes[index].Id : BlueAccentId;

    public static IBrush ResolveThemeBrush(string darkHex, string lightHex)
    {
        var hex = IsLightTheme ? lightHex : darkHex;
        return BrushCache.GetOrAdd(hex, static h => new ImmutableSolidColorBrush(Color.Parse(h)));
    }

    public static void ApplyAccent(string? accent) =>
        ApplyTheme(_currentThemeMode, accent);

    public static void ApplyTheme(string? themeMode, string? accent)
    {
        _currentThemeMode = NormalizeTheme(themeMode);
        _currentAccentId = NormalizeAccent(accent);

        var app = Application.Current;
        if (app is null)
        {
            IsLightTheme = string.Equals(_currentThemeMode, LightThemeId, StringComparison.Ordinal);
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyTheme(_currentThemeMode, _currentAccentId));
            return;
        }

        if (_applying)
        {
            return;
        }

        _applying = true;
        try
        {
            if (!_actualThemeHooked)
            {
                _actualThemeHooked = true;
                app.ActualThemeVariantChanged += (_, _) =>
                {
                    if (string.Equals(_currentThemeMode, SystemThemeId, StringComparison.Ordinal))
                    {
                        ApplyTheme(_currentThemeMode, _currentAccentId);
                    }
                };
            }

            app.RequestedThemeVariant = _currentThemeMode switch
            {
                LightThemeId => ThemeVariant.Light,
                DarkThemeId => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };

            var isLight = _currentThemeMode switch
            {
                LightThemeId => true,
                DarkThemeId => false,
                _ => app.ActualThemeVariant == ThemeVariant.Light
            };

            IsLightTheme = isLight;

            ApplyBaseSurfaceResources(app, isLight);

            var accentIdx = GetAccentIndex(_currentAccentId);
            var palette = isLight ? LightPalettes[accentIdx] : DarkPalettes[accentIdx];
            ApplyAccentResources(app, palette, isLight);
            UpdateBrandDrawings(app, palette, isLight);
        }
        finally
        {
            _applying = false;
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyBaseSurfaceResources(Application app, bool isLight)
    {
        if (isLight)
        {
            SetBrush(app, "M3Surface", "#F8F9FC");
            SetBrush(app, "M3SurfaceContainerLow", "#F2F4F9");
            SetBrush(app, "M3SurfaceContainer", "#ECEFF5");
            SetBrush(app, "M3SurfaceContainerHigh", "#E4E8F0");
            SetBrush(app, "M3SurfaceContainerHighest", "#D9DEE8");
            SetBrush(app, "M3SurfaceHover", "#DFE3EC");

            SetBrush(app, "M3OnSurface", "#1A1C20");
            SetBrush(app, "M3OnSurfaceVariant", "#44474F");
            SetBrush(app, "M3OnSurfaceMuted", "#686C77");
            SetBrush(app, "M3OnSurfaceDisabled", "#9CA0AB");
            SetBrush(app, "M3Outline", "#74777F");
            SetBrush(app, "M3OutlineVariant", "#C4C6D0");
            SetBrush(app, "M3DividerBrush", "#4074777F");

            SetBrush(app, "M3Error", "#B3261E");
            SetBrush(app, "M3ErrorAccent", "#BA1A1A");
            SetBrush(app, "M3OnError", "#FFFFFF");
            SetBrush(app, "M3ErrorContainer", "#FDE2DF");
            SetBrush(app, "M3ErrorContainerHover", "#F9D0CC");
            SetBrush(app, "M3OnErrorContainer", "#5F1412");

            SetBrush(app, "M3Success", "#146C2E");
            SetBrush(app, "M3OnSuccess", "#FFFFFF");
            SetBrush(app, "M3SuccessContainer", "#D7F5DF");
            SetBrush(app, "M3SuccessContainerHover", "#C5EED1");
            SetBrush(app, "M3OnSuccessContainer", "#073B17");

            SetBrush(app, "M3Warning", "#9A5B00");
            SetBrush(app, "M3WarningContainer", "#FFE7BE");
            SetBrush(app, "M3OnWarningContainer", "#4A2800");

            SetBrush(app, "M3Tertiary", "#006495");
            SetBrush(app, "M3StorageTrash", "#74777F");
            SetBrush(app, "M3Scrim", "#66000000");
            SetBrush(app, "M3ModalScrim", "#80111318");

            SetBrush(app, "ComboBoxDropDownBackground", "#FFFFFF");
            SetBrush(app, "ComboBoxDropDownBorderBrush", "#C4C6D0");
            SetBrush(app, "ComboBoxBackground", "#FFFFFF");
            SetBrush(app, "ComboBoxBackgroundPointerOver", "#F2F4F9");
            SetBrush(app, "ComboBoxBorderBrush", "#C4C6D0");
            SetBrush(app, "ComboBoxBorderBrushPointerOver", "#74777F");
            SetBrush(app, "ComboBoxItemBackgroundPointerOver", "#E4E8F0");

            SetBrush(app, "MenuFlyoutPresenterBackground", "#FFFFFF");
            SetBrush(app, "MenuFlyoutPresenterBorderBrush", "#C4C6D0");
            SetBrush(app, "MenuFlyoutItemBackgroundPointerOver", "#ECEFF5");

            SetBrush(app, "TextControlBackground", "#FFFFFF");
            SetBrush(app, "TextControlBackgroundPointerOver", "#F2F4F9");
            SetBrush(app, "TextControlBackgroundFocused", "#FFFFFF");
            SetBrush(app, "TextControlBorderBrush", "#C4C6D0");
            SetBrush(app, "TextControlBorderBrushPointerOver", "#74777F");
            SetBrush(app, "TextControlForeground", "#1A1C20");
            SetBrush(app, "TextControlPlaceholderForeground", "#686C77");

            SetBrush(app, "CheckBoxCheckBackgroundStrokeUnchecked", "#74777F");
            SetBrush(app, "SliderTrackFill", "#D9DEE8");
        }
        else
        {
            SetBrush(app, "M3Surface", "#111318");
            SetBrush(app, "M3SurfaceContainerLow", "#171A1F");
            SetBrush(app, "M3SurfaceContainer", "#1D2024");
            SetBrush(app, "M3SurfaceContainerHigh", "#282A2F");
            SetBrush(app, "M3SurfaceContainerHighest", "#33363D");
            SetBrush(app, "M3SurfaceHover", "#2F3238");

            SetBrush(app, "M3OnSurface", "#E2E2E9");
            SetBrush(app, "M3OnSurfaceVariant", "#C4C6D0");
            SetBrush(app, "M3OnSurfaceMuted", "#9296A4");
            SetBrush(app, "M3OnSurfaceDisabled", "#6B6F7B");
            SetBrush(app, "M3Outline", "#8E9099");
            SetBrush(app, "M3OutlineVariant", "#44474F");
            SetBrush(app, "M3DividerBrush", "#6644474F");

            SetBrush(app, "M3Error", "#FFB4AB");
            SetBrush(app, "M3ErrorAccent", "#F28B82");
            SetBrush(app, "M3OnError", "#690005");
            SetBrush(app, "M3ErrorContainer", "#4A1C20");
            SetBrush(app, "M3ErrorContainerHover", "#5B2328");
            SetBrush(app, "M3OnErrorContainer", "#FFDAD6");

            SetBrush(app, "M3Success", "#6DD58C");
            SetBrush(app, "M3OnSuccess", "#00391B");
            SetBrush(app, "M3SuccessContainer", "#1B3927");
            SetBrush(app, "M3SuccessContainerHover", "#234731");
            SetBrush(app, "M3OnSuccessContainer", "#B8F3C8");

            SetBrush(app, "M3Warning", "#F5C26B");
            SetBrush(app, "M3WarningContainer", "#3B2E14");
            SetBrush(app, "M3OnWarningContainer", "#FFE0A3");

            SetBrush(app, "M3Tertiary", "#7FCFFF");
            SetBrush(app, "M3StorageTrash", "#8E9099");
            SetBrush(app, "M3Scrim", "#99000000");
            SetBrush(app, "M3ModalScrim", "#CC0B0D12");

            SetBrush(app, "ComboBoxDropDownBackground", "#282A2F");
            SetBrush(app, "ComboBoxDropDownBorderBrush", "#44474F");
            SetBrush(app, "ComboBoxBackground", "#282A2F");
            SetBrush(app, "ComboBoxBackgroundPointerOver", "#2F3238");
            SetBrush(app, "ComboBoxBorderBrush", "#44474F");
            SetBrush(app, "ComboBoxBorderBrushPointerOver", "#8E9099");
            SetBrush(app, "ComboBoxItemBackgroundPointerOver", "#33363D");

            SetBrush(app, "MenuFlyoutPresenterBackground", "#282A2F");
            SetBrush(app, "MenuFlyoutPresenterBorderBrush", "#44474F");
            SetBrush(app, "MenuFlyoutItemBackgroundPointerOver", "#33363D");

            SetBrush(app, "TextControlBackground", "#282A2F");
            SetBrush(app, "TextControlBackgroundPointerOver", "#2F3238");
            SetBrush(app, "TextControlBackgroundFocused", "#282A2F");
            SetBrush(app, "TextControlBorderBrush", "#44474F");
            SetBrush(app, "TextControlBorderBrushPointerOver", "#8E9099");
            SetBrush(app, "TextControlForeground", "#E2E2E9");
            SetBrush(app, "TextControlPlaceholderForeground", "#9296A4");

            SetBrush(app, "CheckBoxCheckBackgroundStrokeUnchecked", "#8E9099");
            SetBrush(app, "SliderTrackFill", "#33363D");
        }
    }

    private static void ApplyAccentResources(Application app, AccentPalette palette, bool isLight)
    {
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

        // Keep TextBox/ComboBox overrides aligned with Material3Theme selectors
        SetBrush(app, "TextControlSelectionHighlightColor", palette.Primary);
        if (isLight)
        {
            SetBrush(app, "ComboBoxItemForeground", "#1A1C20");
        }
        else
        {
            SetBrush(app, "ComboBoxItemForeground", "#E2E2E9");
        }
    }

    private static void UpdateBrandDrawings(Application app, AccentPalette palette, bool isLight)
    {
        var onSurface = SolidColorBrush.Parse(isLight ? "#1A1C20" : "#E2E2E9");
        var primary = SolidColorBrush.Parse(palette.Primary);
        var secondaryContainer = SolidColorBrush.Parse(palette.SecondaryContainer);
        var surfaceContainer = SolidColorBrush.Parse(isLight ? "#ECEFF5" : "#1D2024");

        var brandGroup = new DrawingGroup();
        brandGroup.Children.Add(new GeometryDrawing
        {
            Brush = Brushes.Transparent,
            Geometry = Geometry.Parse("M 0,0 H 24 V 24 H 0 Z")
        });
        brandGroup.Children.Add(new GeometryDrawing
        {
            Brush = onSurface,
            Geometry = Geometry.Parse("M 12.0,3.0 L 3.0,12.0 L 12.0,21.0 L 14.0,19.0 L 7.0,12.0 L 14.0,5.0 Z")
        });
        brandGroup.Children.Add(new GeometryDrawing
        {
            Brush = primary,
            Geometry = Geometry.Parse("M 21.0,8.2 L 16.8,4.0 L 8.8,12.0 L 16.8,20.0 L 21.0,15.8 L 19.0,13.8 L 16.8,16.0 L 12.8,12.0 L 16.8,8.0 L 19.0,10.2 Z")
        });
        app.Resources["TundraBrandGlyphDrawing"] = new DrawingImage(brandGroup);

        var shieldGroup = new DrawingGroup();
        shieldGroup.Children.Add(new GeometryDrawing
        {
            Brush = Brushes.Transparent,
            Geometry = Geometry.Parse("M 0,0 H 24 V 24 H 0 Z")
        });
        shieldGroup.Children.Add(new GeometryDrawing
        {
            Brush = secondaryContainer,
            Geometry = Geometry.Parse("M 12.0,3.2 L 18.8,6.4 L 18.8,13.0 L 12.0,20.6 Z")
        });
        shieldGroup.Children.Add(new GeometryDrawing
        {
            Pen = new Pen(primary, 1.8) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
            Geometry = Geometry.Parse("M 12.0,2.6 L 19.4,6.1 L 19.4,13.2 L 12.0,21.4 L 4.6,13.2 L 4.6,6.1 Z")
        });
        shieldGroup.Children.Add(new GeometryDrawing
        {
            Brush = surfaceContainer,
            Pen = new Pen(primary, 1.5) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
            Geometry = Geometry.Parse("M 12.0,7.9 A 2.1,2.1 0 1 1 10.7,11.6 L 10.0,15.8 L 14.0,15.8 L 13.3,11.6 A 2.1,2.1 0 0 1 12.0,7.9 Z")
        });
        app.Resources["TundraShieldVaultDrawing"] = new DrawingImage(shieldGroup);
    }

    private static void SetBrush(Application app, string key, string hex)
    {
        app.Resources[key] = SolidColorBrush.Parse(hex);
    }
}
