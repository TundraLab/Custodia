namespace TundraDrive.Core.Models;

public sealed class LocalSyncFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "default";
    public string LocalPath { get; set; } = string.Empty;
    public string RemoteFolderId { get; set; } = "root";
    public string RemoteFolderName { get; set; } = "Meu Drive";
    public SyncMode Mode { get; set; } = SyncMode.TwoWay;
    public FolderSyncState State { get; set; } = FolderSyncState.Idle;
    public bool IsPaused { get; set; }
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public string? LastErrorMessage { get; set; }
    public int TrackedFileCount { get; set; }
}
