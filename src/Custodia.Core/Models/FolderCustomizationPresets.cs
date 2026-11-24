namespace TundraDrive.Core.Models;

public sealed record FolderColorPreset(string Key, string Hex, string NamePt, string NameEn);

public sealed record FolderIconPreset(string Key, string SvgData, string NamePt, string NameEn);

public static class FolderCustomizationPresets
{
    public const string DefaultColorHex = "#60A5FA";
    public const string DefaultIconKey = "folder";

    public static IReadOnlyList<FolderColorPreset> Colors { get; } =
    [
        new("blue", "#3B82F6", "Azul", "Blue"),
        new("cyan", "#06B6D4", "Ciano", "Cyan"),
        new("emerald", "#10B981", "Verde", "Green"),
        new("amber", "#F59E0B", "Âmbar", "Amber"),
        new("orange", "#F97316", "Laranja", "Orange"),
        new("rose", "#F43F5E", "Rosa", "Rose"),
        new("purple", "#A855F7", "Roxo", "Purple"),
        new("pink", "#EC4899", "Magenta", "Pink")
    ];

    public static IReadOnlyList<FolderIconPreset> Icons { get; } =
    [
        new("folder", "M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z", "Padrão", "Default"),
        new("work", "M20 6h-4V4c0-1.11-.89-2-2-2h-4c-1.11 0-2 .89-2 2v2H4c-1.11 0-1.99.89-1.99 2L2 19c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V8c0-1.11-.89-2-2-2zm-6 0h-4V4h4v2z", "Trabalho", "Work"),
        new("code", "M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z", "Código", "Code"),
        new("photo", "M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z", "Fotos", "Photos"),
        new("music", "M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z", "Músicas", "Music"),
        new("video", "M17 10.5V7c0-.55-.45-1-1-1H4c-.55 0-1 .45-1 1v10c0 .55.45 1 1 1h12c.55 0 1-.45 1-1v-3.5l4 4v-11l-4 4z", "Vídeos", "Videos"),
        new("book", "M18 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zM6 4h5v8l-2.5-1.5L6 12V4z", "Estudos", "Study"),
        new("star", "M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z", "Favorita", "Starred")
    ];

    public static string ResolveIconSvg(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            var match = Icons.FirstOrDefault(i => string.Equals(i.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.SvgData;
            }
        }

        return Icons[0].SvgData;
    }

    public static string? NormalizeColorHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var trimmed = hex.Trim();
        return Colors.FirstOrDefault(c => string.Equals(c.Hex, trimmed, StringComparison.OrdinalIgnoreCase))?.Hex
            ?? (trimmed.StartsWith('#') && (trimmed.Length == 7 || trimmed.Length == 9) ? trimmed : null);
    }
}
