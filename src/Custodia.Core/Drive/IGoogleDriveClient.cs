using TundraDrive.Core.Models;

namespace TundraDrive.Core.Drive;

public interface IGoogleDriveClient
{
    bool IsConnected { get; }
    string ActiveAccountKey { get; }
    Task<DriveAccount> ConnectInteractiveAsync(string userKey = "default", CancellationToken cancellationToken = default);
    Task<DriveAccount?> TryRestoreSessionAsync(string userKey = "default", CancellationToken cancellationToken = default);
    Task<DriveAccount?> SwitchSessionAsync(string userKey, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string? userKey = null, CancellationToken cancellationToken = default);
    Task<DriveAccount> GetAccountInfoAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteDriveNode>> ListChildrenAsync(string parentFolderId = "root", bool foldersOnly = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteDriveNode>> ListAllRemoteFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteDriveNode>> ListStarredFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteDriveNode>> ListTrashedFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteDriveNode>> SearchRemoteFilesAsync(string query, int maxResults = 50, CancellationToken cancellationToken = default);
    Task<RemoteDriveNode?> GetFileMetadataAsync(string remoteFileId, CancellationToken cancellationToken = default);
    Task<RemoteDriveNode> CreateFolderAsync(string folderName, string parentFolderId, CancellationToken cancellationToken = default);
    Task<RemoteDriveNode> FindOrCreateFolderAsync(string folderName, string parentFolderId = "root", CancellationToken cancellationToken = default);
    Task<RemoteDriveNode> UploadFileAsync(string localFilePath, string parentFolderId, string? existingRemoteFileId = null, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default);
    Task<RemoteDriveNode> MoveFileAsync(string remoteFileId, string? destinationParentFolderId = null, string? newName = null, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string remoteFileId, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default);
    Task ExportFileAsync(string remoteFileId, string exportMimeType, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default);
    Task SetStarredAsync(string remoteFileId, bool starred, CancellationToken cancellationToken = default);
    Task TrashFileAsync(string remoteFileId, CancellationToken cancellationToken = default);
    Task RestoreFileAsync(string remoteFileId, CancellationToken cancellationToken = default);
    Task DeletePermanentlyAsync(string remoteFileId, CancellationToken cancellationToken = default);
    Task EmptyTrashAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteDriveNode>> ListSharedWithMeAsync(int maxResults = 200, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RemoteDriveNode>>(Array.Empty<RemoteDriveNode>());

    Task<string?> GetShareLinkAsync(string remoteFileId, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    Task<IReadOnlyList<DrivePermissionEntry>> ListPermissionsAsync(string remoteFileId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DrivePermissionEntry>>(Array.Empty<DrivePermissionEntry>());

    Task<DrivePermissionEntry> ShareAnyoneAsync(string remoteFileId, string role, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DrivePermissionEntry { Id = "anyone", Type = "anyone", Role = role });

    Task<DrivePermissionEntry> InviteUserPermissionAsync(string remoteFileId, string emailAddress, string role, bool sendNotificationEmail = true, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DrivePermissionEntry { Id = Guid.NewGuid().ToString("N"), Type = "user", Role = role, EmailAddress = emailAddress });

    Task RevokePermissionAsync(string remoteFileId, string permissionId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public interface IResumableDriveClient
{
    Task<byte[]?> DownloadThumbnailAsync(string remoteFileId, string? thumbnailLink, CancellationToken cancellationToken = default);

    Task<RemoteDriveNode> ResumableUploadAsync(
        string localFilePath,
        string parentFolderId,
        string? existingRemoteFileId = null,
        string? resumeSessionUri = null,
        Func<string, Task>? onSessionCreated = null,
        Action<long>? onBytesTransferred = null,
        Func<long>? getBandwidthLimitBytesPerSecond = null,
        Sync.TransferBandwidthLimiter? bandwidthLimiter = null,
        CancellationToken cancellationToken = default);

    Task ResumableDownloadAsync(
        string remoteFileId,
        string destinationLocalPath,
        string? resumeTempPath = null,
        long knownTotalBytes = 0,
        Func<string, Task>? onTempPathAssigned = null,
        Action<long>? onBytesTransferred = null,
        Func<long>? getBandwidthLimitBytesPerSecond = null,
        Sync.TransferBandwidthLimiter? bandwidthLimiter = null,
        bool keepTempOnInterrupt = true,
        CancellationToken cancellationToken = default);
}
