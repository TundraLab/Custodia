using System.Reflection;
using Microsoft.Extensions.Logging;

namespace TundraDrive.Core.Configuration;

public sealed class LinuxAutostartManager : IAutostartManager
{
    private readonly ILogger<LinuxAutostartManager> _logger;
    private readonly string _legacyDesktopEntryPath;

    public LinuxAutostartManager(ILogger<LinuxAutostartManager> logger)
    {
        _logger = logger;
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var autostartDir = Path.Combine(userHome, ".config", "autostart");
        DesktopEntryPath = Path.Combine(autostartDir, "custodia.desktop");
        _legacyDesktopEntryPath = Path.Combine(autostartDir, "tundradrive.desktop");
    }

    public string DesktopEntryPath { get; }
    public bool IsSupported => OperatingSystem.IsLinux();

    public bool IsEnabled() => IsSupported && (File.Exists(DesktopEntryPath) || File.Exists(_legacyDesktopEntryPath));

    public void SetEnabled(bool enabled)
    {
        if (!IsSupported)
        {
            return;
        }

        if (File.Exists(_legacyDesktopEntryPath))
        {
            try
            {
                File.Delete(_legacyDesktopEntryPath);
            }
            catch
            {
            }
        }

        if (!enabled)
        {
            if (File.Exists(DesktopEntryPath))
            {
                File.Delete(DesktopEntryPath);
                _logger.LogInformation("Removed XDG autostart entry at '{Path}'.", DesktopEntryPath);
            }
            return;
        }

        var dir = Path.GetDirectoryName(DesktopEntryPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var installedPngIcon = Path.Combine(userHome, ".local", "share", "icons", "hicolor", "256x256", "apps", "custodia.png");
        var iconEntry = File.Exists(installedPngIcon) ? installedPngIcon : "custodia";
        var execCommand = ResolveExecCommand();
        var content = $"""
            [Desktop Entry]
            Type=Application
            Version=1.0
            Name=Custódia
            Comment=Custódia by TundraLab - Seus arquivos. Sempre seguros.
            Icon={iconEntry}
            Exec={execCommand}
            Terminal=false
            Categories=Utility;
            StartupWMClass=Custodia
            X-GNOME-Autostart-enabled=true
            X-GNOME-Autostart-Delay=4
            X-KDE-autostart-after=panel
            StartupNotify=false
            """;

        var existingContent = File.Exists(DesktopEntryPath)
            ? File.ReadAllText(DesktopEntryPath)
            : null;
        if (string.Equals(existingContent?.Trim(), content.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(DesktopEntryPath, content);
        _logger.LogInformation("Created XDG autostart entry at '{Path}'.", DesktopEntryPath);
    }

    private static string ResolveExecCommand()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            (processPath.StartsWith("/opt/custodia/", StringComparison.Ordinal) ||
             processPath.StartsWith("/opt/tundradrive/", StringComparison.Ordinal)))
        {
            if (File.Exists("/usr/bin/custodia"))
            {
                return "\"/usr/bin/custodia\"";
            }

            if (File.Exists("/usr/bin/tundradrive"))
            {
                return "\"/usr/bin/tundradrive\"";
            }

            return $"\"{processPath}\"";
        }

        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImagePath) &&
            !appImagePath.Contains("/.cache/custodia-installer/", StringComparison.Ordinal) &&
            !appImagePath.Contains("/.cache/tundradrive-installer/", StringComparison.Ordinal))
        {
            return $"\"{appImagePath}\"";
        }

        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;

        if (!string.IsNullOrWhiteSpace(processPath) &&
            Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return $"\"{processPath}\" \"{entryAssemblyPath}\"";
        }

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return $"\"{processPath}\"";
        }

        return "custodia";
    }
}
