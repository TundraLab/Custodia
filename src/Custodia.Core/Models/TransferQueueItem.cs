namespace TundraDrive.Core.Models;

public sealed class TransferQueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SyncFolderId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string? RemoteFileId { get; set; }
    public string? RemoteParentId { get; set; }
    public bool IsGoogleWorkspaceDoc { get; set; }
    public string? MimeType { get; set; }
    public TransferDirection Direction { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Queued;
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public bool CanRetry =>
        Status is TransferStatus.Failed or TransferStatus.Cancelled &&
        Direction is TransferDirection.Upload or TransferDirection.Download;

    public double ProgressPercentage =>
        TotalBytes <= 0
            ? (Status == TransferStatus.Completed ? 100.0 : 0.0)
            : Math.Clamp((double)TransferredBytes / TotalBytes * 100.0, 0.0, 100.0);
}

public sealed class TransferResumeSession
{
    public string TransferKey { get; set; } = string.Empty;
    public TransferDirection Direction { get; set; }
    public string LocalPath { get; set; } = string.Empty;
    public string? RemoteFileId { get; set; }
    public string? RemoteParentId { get; set; }
    public string? UploadSessionUri { get; set; }
    public string? TempFilePath { get; set; }
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
    public long LocalMtimeTicks { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record TransferQueueStats(
    int ActiveCount,
    int CompletedCount,
    int FailedCount,
    int CancelledCount,
    int TotalCount);
