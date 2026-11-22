using System.Diagnostics;
using System.Reflection;
using Avalonia.Platform;

namespace TundraDrive.Desktop.Services;

public static class LinuxDesktopIntegrationHelper
{
    private static readonly string UserHome =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string InstalledPngIconPath =>
        Path.Combine(UserHome, ".local", "share", "icons", "hicolor", "256x256", "apps", "tundradrive.png");

    public static string InstalledSvgIconPath =>
        Path.Combine(UserHome, ".local", "share", "icons", "hicolor", "scalable", "apps", "tundradrive.svg");

    public static string DesktopEntryPath =>
        Path.Combine(UserHome, ".local", "share", "applications", "tundradrive.desktop");

    public static void InstallDesktopAssets()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            CopyAssetToFile("avares://TundraDrive/Assets/tundradrive-icon.png", InstalledPngIconPath);
            CopyAssetToFile("avares://TundraDrive/Assets/tundradrive-icon.svg", InstalledSvgIconPath);

            var appsDir = Path.GetDirectoryName(DesktopEntryPath);
            if (!string.IsNullOrWhiteSpace(appsDir))
            {
                Directory.CreateDirectory(appsDir);
            }

            var execCommand = ResolveExecCommand();
            var desktopContent = $"""
                [Desktop Entry]
                Type=Application
                Version=1.0
                Name=TundraDrive
                Comment=Google Drive Local Synchronization Client
                Icon={InstalledPngIconPath}
                Exec={execCommand}
                Terminal=false
                Categories=Network;Utility;
                StartupWMClass=TundraDrive
                """;

            File.WriteAllText(DesktopEntryPath, desktopContent);
            RefreshDesktopCachesInBackground(appsDir);
        }
        catch
        {
            // Non-fatal desktop integration
        }
    }

    private static void RefreshDesktopCachesInBackground(string? appsDir)
    {
        _ = Task.Run(() =>
        {
            if (!string.IsNullOrWhiteSpace(appsDir))
            {
                TryRunTool("update-desktop-database", appsDir);
            }

            var hicolorDir = Path.Combine(UserHome, ".local", "share", "icons", "hicolor");
            if (Directory.Exists(hicolorDir))
            {
                TryRunTool("gtk-update-icon-cache", "-f", "-t", hicolorDir);
            }

            // KDE Plasma 6 / 5 service cache refresh so icon and .desktop bind right away
            if (!TryRunTool("kbuildsycoca6", "--noincremental"))
            {
                TryRunTool("kbuildsycoca5", "--noincremental");
            }
        });
    }

    private static bool TryRunTool(string executable, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyAssetToFile(string avaresUri, string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var src = AssetLoader.Open(new Uri(avaresUri));
        using var dst = File.Create(targetPath);
        src.CopyTo(dst);
    }

    private static string ResolveExecCommand()
    {
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImagePath))
        {
            return $"\"{appImagePath}\"";
        }

        var processPath = Environment.ProcessPath;
        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;

        if (!string.IsNullOrWhiteSpace(processPath) &&
            Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            return $"\"{processPath}\" \"{entryAssemblyPath}\"";
        }

        return !string.IsNullOrWhiteSpace(processPath) ? $"\"{processPath}\"" : "tundradrive";
    }
}
