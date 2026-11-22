namespace TundraDrive.Core.Models;

public sealed class TrackedFileEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SyncFolderId { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string RemoteFileId { get; set; } = string.Empty;
    public string RemoteParentId { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public string? Md5Hash { get; set; }
    public string? Sha256Hash { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset LocalModifiedAtUtc { get; set; }
    public DateTimeOffset RemoteModifiedAtUtc { get; set; }
    public DateTimeOffset LastSyncedAtUtc { get; set; }
}
