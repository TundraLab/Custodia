namespace TundraDrive.Api.Contracts;

public sealed record ApiStatusResponse(
    string ServiceName,
    string BoundEndpoint,
    bool IsAccountConnected,
    string? AccountEmail,
    bool IsSyncRunning,
    long StorageUsageBytes,
    long StorageLimitBytes,
    int ActiveTransferCount,
    IReadOnlyList<SyncFolderSummaryDto> SyncFolders);

public sealed record SyncFolderSummaryDto(
    string Id,
    string LocalPath,
    string RemoteFolderId,
    string RemoteFolderName,
    string SyncMode,
    string State,
    bool IsPaused,
    DateTimeOffset? LastSyncedAtUtc,
    int TrackedFileCount);

public sealed record TriggerSyncRequest(
    string? FolderId = null,
    bool WaitForCompletion = false);

public sealed record TriggerSyncResponse(
    bool Accepted,
    string Scope,
    string Message);

public sealed record FileSearchItemDto(
    string Source,
    string? SyncFolderId,
    string Name,
    string RelativeOrRemotePath,
    string RemoteFileId,
    long SizeBytes,
    string? Md5Hash,
    DateTimeOffset ModifiedAtUtc);

public sealed record FileSearchResponse(
    string Query,
    int TotalCount,
    IReadOnlyList<FileSearchItemDto> Items);

public sealed record UploadFileRequest(
    string LocalPath,
    string? RemoteFolderId = null);

public sealed record UploadFileResponse(
    bool Success,
    string RemoteFileId,
    string FileName,
    long SizeBytes,
    string? Md5Checksum);

public sealed record DownloadFileRequest(
    string RemoteFileId,
    string? DestinationPath = null);

public sealed record DownloadFileResponse(
    bool Success,
    string RemoteFileId,
    string FileName,
    string SavedPath,
    long SizeBytes);

public sealed record MoveFileRequest(
    string? TargetFolderId = null,
    string? NewName = null);

public sealed record MoveFileResponse(
    bool Success,
    string RemoteFileId,
    string FileName,
    IReadOnlyList<string> ParentIds);

public sealed record CreateFolderRequest(
    string Name,
    string? ParentFolderId = null);

public sealed record DriveNodeDto(
    string Id,
    string Name,
    string MimeType,
    bool IsFolder,
    long SizeBytes,
    bool Trashed,
    IReadOnlyList<string> ParentIds,
    DateTimeOffset ModifiedAtUtc);

public sealed record FileListResponse(
    string Scope,
    int TotalCount,
    IReadOnlyList<DriveNodeDto> Items);

public sealed record FileActionResponse(
    bool Success,
    string RemoteFileId,
    string Action);

public sealed record TransferItemDto(
    string Id,
    string SyncFolderId,
    string FileName,
    string RelativePath,
    string Direction,
    string Status,
    long TotalBytes,
    long TransferredBytes,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record TransfersResponse(
    int TotalCount,
    int ActiveCount,
    IReadOnlyList<TransferItemDto> Items);

public sealed record ActivityLogDto(
    long Id,
    string? SyncFolderId,
    string Severity,
    string Action,
    string Message,
    string? TargetPath,
    DateTimeOffset CreatedAtUtc);

public sealed record ActivityLogsResponse(
    int TotalCount,
    IReadOnlyList<ActivityLogDto> Items);
