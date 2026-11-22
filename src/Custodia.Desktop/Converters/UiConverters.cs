using System.Globalization;
using Avalonia.Data.Converters;

namespace TundraDrive.Desktop.Converters;

public static class UiConverters
{
    public static readonly IValueConverter BoolToOpacityConverter =
        new FuncValueConverter<bool, double>(value => value ? 1.0 : 0.45);

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return string.Format(CultureInfo.GetCultureInfo("pt-BR"), "{0:0.##} {1}", size, units[unitIndex]);
    }

    public static string FormatRelativeTimestamp(DateTimeOffset? utcTimestamp)
    {
        if (!utcTimestamp.HasValue)
        {
            return "Nunca sincronizado";
        }

        var local = utcTimestamp.Value.ToLocalTime();
        var elapsed = DateTimeOffset.Now - local;

        if (elapsed.TotalSeconds < 60)
        {
            return "Agora mesmo";
        }

        if (elapsed.TotalMinutes < 60)
        {
            var mins = (int)elapsed.TotalMinutes;
            return mins == 1 ? "Há 1 minuto" : $"Há {mins} minutos";
        }

        if (elapsed.TotalHours < 24)
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "Há 1 hora" : $"Há {hours} horas";
        }

        return local.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
    }
}
