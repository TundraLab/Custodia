using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Avalonia.Platform;
using Tmds.DBus.Protocol;

namespace TundraDrive.Desktop.Services;

public static class LinuxDesktopIntegrationHelper
{
    private const string DbusService = "org.freedesktop.DBus";
    private const string DbusPath = "/org/freedesktop/DBus";
    private const string DbusInterface = "org.freedesktop.DBus";
    private const string StatusNotifierWatcherName = "org.kde.StatusNotifierWatcher";
    private const double MinimumWatcherProcessAgeSeconds = 10.0;
    private const double WatcherNewlyAppearedSettleSeconds = 4.0;

    private static readonly string UserHome =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string InstalledPngIconPath =>
        Path.Combine(UserHome, ".local", "share", "icons", "hicolor", "256x256", "apps", "custodia.png");

    public static string InstalledSvgIconPath =>
        Path.Combine(UserHome, ".local", "share", "icons", "hicolor", "scalable", "apps", "custodia.svg");

    public static string DesktopEntryPath =>
        Path.Combine(UserHome, ".local", "share", "applications", "custodia.desktop");

    private static string LegacyDesktopEntryPath =>
        Path.Combine(UserHome, ".local", "share", "applications", "tundradrive.desktop");

    public static void InstallDesktopAssets()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            var iconsChanged = false;
            iconsChanged |= CopyAssetToFileIfMissing("avares://Custodia/Assets/custodia-icon.png", InstalledPngIconPath);
            iconsChanged |= CopyAssetToFileIfMissing("avares://Custodia/Assets/custodia-icon.svg", InstalledSvgIconPath);

            var appsDir = Path.GetDirectoryName(DesktopEntryPath);
            if (File.Exists(LegacyDesktopEntryPath))
            {
                try
                {
                    File.Delete(LegacyDesktopEntryPath);
                    iconsChanged = true;
                }
                catch
                {
                }
            }

            var processPath = Environment.ProcessPath ?? string.Empty;
            var isSystemRpmInstall = (processPath.StartsWith("/opt/custodia/", StringComparison.Ordinal) &&
                                      File.Exists("/usr/share/applications/custodia.desktop")) ||
                                     (processPath.StartsWith("/opt/tundradrive/", StringComparison.Ordinal) &&
                                      File.Exists("/usr/share/applications/tundradrive.desktop"));

            if (isSystemRpmInstall)
            {
                if (File.Exists(DesktopEntryPath))
                {
                    File.Delete(DesktopEntryPath);
                    RefreshDesktopCachesInBackground(appsDir);
                }
                else if (iconsChanged)
                {
                    RefreshDesktopCachesInBackground(appsDir);
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(appsDir))
            {
                Directory.CreateDirectory(appsDir);
            }

            var execCommand = ResolveExecCommand();
            var desktopContent = $"""
                [Desktop Entry]
                Type=Application
                Version=1.0
                Name=Custódia
                Comment=Custódia by TundraLab - Seus arquivos. Sempre seguros.
                Icon={InstalledPngIconPath}
                Exec={execCommand}
                Terminal=false
                Categories=Network;Utility;
                StartupWMClass=Custodia
                """;

            var existingContent = File.Exists(DesktopEntryPath)
                ? File.ReadAllText(DesktopEntryPath)
                : null;

            if (!string.Equals(existingContent?.Trim(), desktopContent.Trim(), StringComparison.Ordinal))
            {
                File.WriteAllText(DesktopEntryPath, desktopContent);
                RefreshDesktopCachesInBackground(appsDir);
            }
            else if (iconsChanged)
            {
                RefreshDesktopCachesInBackground(null);
            }
        }
        catch
        {
            // Non-fatal desktop integration
        }
    }

    public static async Task WaitForStatusNotifierWatcherReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var sessionAddress = Address.Session;
        if (string.IsNullOrWhiteSpace(sessionAddress))
        {
            return;
        }

        try
        {
            using var conn = new Connection(sessionAddress);
            await conn.ConnectAsync();

            var sawMissingWatcher = false;
            uint watcherPid = 0;
            var deadline = DateTime.UtcNow.AddSeconds(15);

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                watcherPid = await TryGetStatusNotifierWatcherPidAsync(conn);
                if (watcherPid > 0)
                {
                    break;
                }

                sawMissingWatcher = true;
                await Task.Delay(400, cancellationToken);
            }

            if (watcherPid == 0)
            {
                return;
            }

            var requiredDelaySeconds = sawMissingWatcher ? WatcherNewlyAppearedSettleSeconds : 0.0;
            if (TryGetProcessAgeSeconds(watcherPid, out var ageSeconds) &&
                ageSeconds < MinimumWatcherProcessAgeSeconds)
            {
                var remainingAgeDelay = MinimumWatcherProcessAgeSeconds - ageSeconds;
                if (remainingAgeDelay > requiredDelaySeconds)
                {
                    requiredDelaySeconds = remainingAgeDelay;
                }
            }

            if (requiredDelaySeconds > 0.05)
            {
                var clampedDelay = TimeSpan.FromSeconds(Math.Clamp(requiredDelaySeconds, 0.25, 12.0));
                await Task.Delay(clampedDelay, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Fall back to immediate tray creation if D-Bus inspection fails
        }
    }

    private static async Task<uint> TryGetStatusNotifierWatcherPidAsync(Connection conn)
    {
        try
        {
            var writer = conn.GetMessageWriter();
            MessageBuffer message;
            try
            {
                writer.WriteMethodCallHeader(
                    destination: DbusService,
                    path: DbusPath,
                    @interface: DbusInterface,
                    member: "GetConnectionUnixProcessID",
                    signature: "s");
                writer.WriteString(StatusNotifierWatcherName);
                message = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }

            return await conn.CallMethodAsync(
                message,
                static (Message m, object? _) => m.GetBodyReader().ReadUInt32(),
                null);
        }
        catch
        {
            return 0;
        }
    }

    internal static bool TryGetProcessAgeSeconds(uint pid, out double ageSeconds)
    {
        ageSeconds = 0;
        if (pid == 0 || !OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            var statPath = $"/proc/{pid}/stat";
            if (!File.Exists("/proc/uptime") || !File.Exists(statPath))
            {
                return false;
            }

            var uptimeRaw = File.ReadAllText("/proc/uptime");
            var uptimeToken = uptimeRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!double.TryParse(uptimeToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var uptimeSeconds))
            {
                return false;
            }

            var statRaw = File.ReadAllText(statPath);
            var closeParen = statRaw.LastIndexOf(')');
            if (closeParen < 0 || closeParen + 2 >= statRaw.Length)
            {
                return false;
            }

            var fieldsAfterComm = statRaw[(closeParen + 2)..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fieldsAfterComm.Length <= 19 ||
                !long.TryParse(fieldsAfterComm[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startTicks))
            {
                return false;
            }

            const double clockTicksPerSecond = 100.0;
            var startSeconds = startTicks / clockTicksPerSecond;
            ageSeconds = Math.Max(0.0, uptimeSeconds - startSeconds);
            return true;
        }
        catch
        {
            return false;
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

    private static bool CopyAssetToFileIfMissing(string avaresUri, string targetPath)
    {
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
        {
            return false;
        }

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var src = AssetLoader.Open(new Uri(avaresUri));
        using var dst = File.Create(targetPath);
        src.CopyTo(dst);
        return true;
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

        return !string.IsNullOrWhiteSpace(processPath) ? $"\"{processPath}\"" : "custodia";
    }
}
