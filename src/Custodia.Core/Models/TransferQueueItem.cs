namespace TundraDrive.Core.Models;

public sealed class TransferQueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SyncFolderId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string? RemoteFileId { get; set; }
    public TransferDirection Direction { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Queued;
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public double ProgressPercentage =>
        TotalBytes <= 0
            ? (Status == TransferStatus.Completed ? 100.0 : 0.0)
            : Math.Clamp((double)TransferredBytes / TotalBytes * 100.0, 0.0, 100.0);
}
