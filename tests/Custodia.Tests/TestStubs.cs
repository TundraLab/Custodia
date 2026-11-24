using System.Reflection;
using TundraDrive.Core.Models;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Services;

namespace TundraDrive.Tests;

internal sealed class RecordingNotificationService : IDesktopNotificationService
{
    public List<(string Title, string Body, NotificationUrgency Urgency, bool Bypass, string? TargetPath)> Calls { get; } = [];

    public Task NotifyAsync(
        string title,
        string body,
        NotificationUrgency urgency = NotificationUrgency.Normal,
        bool bypassThrottle = false,
        string? targetPath = null)
    {
        Calls.Add((title, body, urgency, bypassThrottle, targetPath));
        return Task.CompletedTask;
    }
}

internal class StubDriveSyncProxy : DispatchProxy
{
    public DriveAccount Account { get; set; } = new() { IsConnected = true, EmailAddress = "user@test.com" };
    public List<string> DownloadedIds { get; } = [];
    public List<string> TrashedIds { get; } = [];
    public List<(bool AddNew, string? CustomJson)> ConnectCalls { get; } = [];

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == $"get_{nameof(IDriveSyncService.CurrentAccount)}")
            return Account;
        if (targetMethod?.Name == $"get_{nameof(IDriveSyncService.SavedAccounts)}")
            return Array.Empty<DriveAccount>();
        if (targetMethod?.Name == $"get_{nameof(IDriveSyncService.IsOnline)}")
            return true;
        if (targetMethod?.Name == $"get_{nameof(IDriveSyncService.IsSyncRunning)}")
            return false;
        if (targetMethod?.Name == nameof(IDriveSyncService.ConnectAccountAsync))
        {
            ConnectCalls.Add(((bool)args![0]!, (string?)args[1]));
            return Task.FromResult(Account);
        }
        if (targetMethod?.Name == nameof(IDriveSyncService.ReconnectAccountAsync))
            return Task.FromResult(Account);
        if (targetMethod?.Name == nameof(IDriveSyncService.GetTransferQueueSnapshot))
            return Array.Empty<TransferQueueItem>();
        if (targetMethod?.Name == nameof(IDriveSyncService.GetTransferQueueStats))
            return new TransferQueueStats(0, 0, 0, 0, 0);
        if (targetMethod?.Name == nameof(IDriveSyncService.GetSyncFoldersAsync))
            return Task.FromResult<IReadOnlyList<LocalSyncFolder>>(Array.Empty<LocalSyncFolder>());
        if (targetMethod?.Name == nameof(IDriveSyncService.UpdateFolderCustomizationAsync))
            return Task.CompletedTask;
        if (targetMethod?.Name == nameof(IDriveSyncService.DownloadRemoteFileAsync))
        {
            DownloadedIds.Add((string)args![0]!);
            return Task.FromResult($"/tmp/{args[1]}");
        }
        if (targetMethod?.Name == nameof(IDriveSyncService.TrashRemoteItemAsync))
        {
            TrashedIds.Add((string)args![0]!);
            return Task.CompletedTask;
        }
        if (targetMethod?.Name == nameof(IDriveSyncService.RefreshAccountInfoAsync))
            return Task.FromResult<DriveAccount?>(Account);
        return null;
    }
}

internal class StubDriveClientProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "get_IsConnected")
            return true;
        return null;
    }
}

internal class StubStateRepoProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
}
