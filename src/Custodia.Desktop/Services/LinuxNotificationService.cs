using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using TundraDrive.Core.Configuration;
using TundraDrive.Desktop.Localization;

namespace TundraDrive.Desktop.Services;

public sealed class LinuxNotificationService : IDesktopNotificationService, IDisposable
{
    private const string NotificationsService = "org.freedesktop.Notifications";
    private const string NotificationsPath = "/org/freedesktop/Notifications";
    private const string NotificationsInterface = "org.freedesktop.Notifications";

    private readonly IAppSettingsService _settingsService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<LinuxNotificationService> _logger;
    private readonly object _throttleLock = new();
    private readonly SemaphoreSlim _dbusLock = new(1, 1);
    private readonly ConcurrentDictionary<uint, string> _pendingActionTargets = new();

    private Connection? _dbusConnection;
    private IDisposable? _actionInvokedSub;
    private IDisposable? _notificationClosedSub;
    private DateTimeOffset _lastNotificationUtc = DateTimeOffset.MinValue;
    private string _lastKey = string.Empty;
    private bool _disposed;

    public LinuxNotificationService(
        IAppSettingsService settingsService,
        ILocalizationService localization,
        ILogger<LinuxNotificationService> logger)
    {
        _settingsService = settingsService;
        _localization = localization;
        _logger = logger;
    }

    public async Task NotifyAsync(
        string title,
        string body,
        NotificationUrgency urgency = NotificationUrgency.Normal,
        bool bypassThrottle = false,
        string? targetPath = null)
    {
        if (!OperatingSystem.IsLinux() || !_settingsService.Current.NotificationsEnabled || _disposed)
        {
            return;
        }

        if (!bypassThrottle)
        {
            var now = DateTimeOffset.UtcNow;
            lock (_throttleLock)
            {
                if (string.Equals(_lastKey, title, StringComparison.Ordinal) &&
                    (now - _lastNotificationUtc).TotalSeconds < 5)
                {
                    return;
                }

                _lastKey = title;
                _lastNotificationUtc = now;
            }
        }

        var iconPath = LinuxDesktopIntegrationHelper.InstalledPngIconPath;
        var iconArg = File.Exists(iconPath) ? iconPath : "custodia";
        var hasTarget = !string.IsNullOrWhiteSpace(targetPath) &&
                        (File.Exists(targetPath) || Directory.Exists(targetPath));

        if (await TryNotifyViaDbusAsync(title, body, iconArg, urgency, hasTarget ? targetPath : null))
        {
            return;
        }

        await NotifyViaCliFallbackAsync(title, body, iconArg, urgency, hasTarget ? targetPath : null);
    }

    private async Task<bool> TryNotifyViaDbusAsync(
        string title,
        string body,
        string iconArg,
        NotificationUrgency urgency,
        string? targetPath)
    {
        try
        {
            var conn = await EnsureDbusConnectionAsync();
            if (conn is null)
            {
                return false;
            }

            var actions = string.IsNullOrWhiteSpace(targetPath)
                ? Array.Empty<string>()
                : new[]
                {
                    "default", _localization.IsEnglish ? "Open folder" : "Abrir pasta",
                    "open", _localization.IsEnglish ? "Open folder" : "Abrir pasta"
                };

            byte urgencyByte = urgency switch
            {
                NotificationUrgency.Low => 0,
                NotificationUrgency.Critical => 2,
                _ => 1
            };

            var notificationId = await conn.CallMethodAsync(
                BuildNotifyMessage(conn, iconArg, title, body, actions, urgencyByte),
                (Message m, object? _) => m.GetBodyReader().ReadUInt32(),
                null);

            if (!string.IsNullOrWhiteSpace(targetPath) && notificationId > 0)
            {
                _pendingActionTargets[notificationId] = targetPath;
                PrunePendingActions();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "D-Bus desktop notification failed; falling back to notify-send.");
            ResetDbusConnection();
            return false;
        }
    }

    private async Task<Connection?> EnsureDbusConnectionAsync()
    {
        var sessionAddress = Address.Session;
        if (string.IsNullOrWhiteSpace(sessionAddress))
        {
            return null;
        }

        if (_dbusConnection is not null)
        {
            return _dbusConnection;
        }

        await _dbusLock.WaitAsync();
        try
        {
            if (_disposed)
            {
                return null;
            }

            if (_dbusConnection is not null)
            {
                return _dbusConnection;
            }

            var conn = new Connection(sessionAddress);
            await conn.ConnectAsync();

            _actionInvokedSub = await conn.AddMatchAsync<(uint Id, string ActionKey)>(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = NotificationsInterface,
                    Path = NotificationsPath,
                    Member = "ActionInvoked"
                },
                (Message m, object? state) =>
                {
                    var reader = m.GetBodyReader();
                    return (reader.ReadUInt32(), reader.ReadString());
                },
                (Exception? ex, (uint Id, string ActionKey) payload, object? state1, object? state2) =>
                {
                    if (ex is not null)
                    {
                        return;
                    }

                    if (_pendingActionTargets.TryRemove(payload.Id, out var path) &&
                        !string.IsNullOrWhiteSpace(path))
                    {
                        _ = OpenInFileManagerAsync(path);
                    }
                },
                ObserverFlags.None);

            _notificationClosedSub = await conn.AddMatchAsync<uint>(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = NotificationsInterface,
                    Path = NotificationsPath,
                    Member = "NotificationClosed"
                },
                (Message m, object? state) => m.GetBodyReader().ReadUInt32(),
                (Exception? ex, uint closedId, object? state1, object? state2) =>
                {
                    if (ex is null)
                    {
                        _pendingActionTargets.TryRemove(closedId, out var _);
                    }
                },
                ObserverFlags.None);

            _dbusConnection = conn;
            return conn;
        }
        finally
        {
            _dbusLock.Release();
        }
    }

    private static MessageBuffer BuildNotifyMessage(
        Connection conn,
        string iconArg,
        string title,
        string body,
        string[] actions,
        byte urgencyByte)
    {
        var writer = conn.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(
                destination: NotificationsService,
                path: NotificationsPath,
                @interface: NotificationsInterface,
                member: "Notify",
                signature: "susssasa{sv}i");
            writer.WriteString("Custódia");
            writer.WriteUInt32(0);
            writer.WriteString(iconArg);
            writer.WriteString(title);
            writer.WriteString(body);
            writer.WriteArray(actions);
            writer.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["urgency"] = VariantValue.Byte(urgencyByte),
                ["desktop-entry"] = VariantValue.String("custodia")
            });
            writer.WriteInt32(-1);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private void PrunePendingActions()
    {
        if (_pendingActionTargets.Count <= 128)
        {
            return;
        }

        foreach (var oldest in _pendingActionTargets.Keys.OrderBy(k => k).Take(_pendingActionTargets.Count - 64))
        {
            _pendingActionTargets.TryRemove(oldest, out _);
        }
    }

    private void ResetDbusConnection()
    {
        try
        {
            _actionInvokedSub?.Dispose();
            _notificationClosedSub?.Dispose();
            _dbusConnection?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _actionInvokedSub = null;
            _notificationClosedSub = null;
            _dbusConnection = null;
        }
    }

    private async Task NotifyViaCliFallbackAsync(
        string title,
        string body,
        string iconArg,
        NotificationUrgency urgency,
        string? targetPath)
    {
        var urgencyArg = urgency switch
        {
            NotificationUrgency.Low => "low",
            NotificationUrgency.Critical => "critical",
            _ => "normal"
        };

        var hasTarget = !string.IsNullOrWhiteSpace(targetPath);
        if (hasTarget)
        {
            var actionLabel = _localization.IsEnglish ? "Open folder" : "Abrir pasta";
            var actionPsi = BuildNotifySendStartInfo(iconArg, urgencyArg, title, body, actionLabel);
            _ = Task.Run(async () =>
            {
                var ok = await DispatchAndListenForClickAsync(actionPsi, targetPath!);
                if (!ok)
                {
                    await RunSimpleNotifySendAsync(iconArg, urgencyArg, title, body);
                }
            });
            return;
        }

        await RunSimpleNotifySendAsync(iconArg, urgencyArg, title, body);
    }

    private static ProcessStartInfo BuildNotifySendStartInfo(
        string iconArg,
        string urgencyArg,
        string title,
        string body,
        string? actionLabel)
    {
        var psi = new ProcessStartInfo("notify-send")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("--app-name=Custódia");
        psi.ArgumentList.Add($"--icon={iconArg}");
        psi.ArgumentList.Add($"--urgency={urgencyArg}");
        if (!string.IsNullOrWhiteSpace(actionLabel))
        {
            psi.ArgumentList.Add($"--action=default={actionLabel}");
            psi.ArgumentList.Add($"--action=open={actionLabel}");
        }

        psi.ArgumentList.Add(title);
        psi.ArgumentList.Add(body);
        return psi;
    }

    private async Task RunSimpleNotifySendAsync(string iconArg, string urgencyArg, string title, string body)
    {
        try
        {
            var psi = BuildNotifySendStartInfo(iconArg, urgencyArg, title, body, actionLabel: null);
            using var process = Process.Start(psi);
            if (process is not null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not dispatch desktop notification via notify-send.");
        }
    }

    private async Task<bool> DispatchAndListenForClickAsync(ProcessStartInfo startInfo, string targetPath)
    {
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var action = (await process.StandardOutput.ReadToEndAsync(cts.Token)).Trim();
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                await OpenInFileManagerAsync(targetPath);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Notification action listener ended.");
            return false;
        }
    }

    private async Task OpenInFileManagerAsync(string targetPath)
    {
        try
        {
            if (File.Exists(targetPath))
            {
                var uri = new Uri(Path.GetFullPath(targetPath)).AbsoluteUri;
                if (await TryShowItemsViaDbusAsync(uri))
                {
                    return;
                }

                if (TryStartDetached("dolphin", "--select", targetPath))
                {
                    return;
                }
            }

            var folderPath = Directory.Exists(targetPath)
                ? targetPath
                : Path.GetDirectoryName(targetPath);

            if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                TryStartDetached("xdg-open", folderPath);
            }
        }
        catch
        {
        }
    }

    private async Task<bool> TryShowItemsViaDbusAsync(string fileUri)
    {
        try
        {
            var conn = await EnsureDbusConnectionAsync();
            if (conn is null)
            {
                return false;
            }

            await conn.CallMethodAsync(BuildShowItemsMessage(conn, fileUri));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "D-Bus org.freedesktop.FileManager1.ShowItems failed.");
            return false;
        }
    }

    private static MessageBuffer BuildShowItemsMessage(Connection conn, string fileUri)
    {
        var writer = conn.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.FileManager1",
                path: "/org/freedesktop/FileManager1",
                @interface: "org.freedesktop.FileManager1",
                member: "ShowItems",
                signature: "ass");
            writer.WriteArray(new[] { fileUri });
            writer.WriteString(string.Empty);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static bool TryStartDetached(string executable, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi);
            return proc is not null;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        ResetDbusConnection();
        _dbusLock.Dispose();
    }
}
