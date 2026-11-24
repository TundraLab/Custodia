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
    Task<IReadOnlyList<RemoteDriveNode>> ListTrashedFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemoteDriveNode>> SearchRemoteFilesAsync(string query, int maxResults = 50, CancellationToken cancellationToken = default);
    Task<RemoteDriveNode?> GetFileMetadataAsync(string remoteFileId, CancellationToken cancellationToken = default);
    Task<RemoteDriveNode> CreateFolderAsync(string folderName, string parentFolderId, CancellationToken cancellationToken = default);
    Task<RemoteDriveNode> FindOrCreateFolderAsync(string folderName, string parentFolderId = "root", CancellationToken cancellationToken = default);
    Task<RemoteDriveNode> UploadFileAsync(string localFilePath, string parentFolderId, string? existingRemoteFileId = null, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default);
    Task<RemoteDriveNode> MoveFileAsync(string remoteFileId, string? destinationParentFolderId = null, string? newName = null, CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string remoteFileId, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default);
    Task ExportFileAsync(string remoteFileId, string exportMimeType, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default);
    Task TrashFileAsync(string remoteFileId, CancellationToken cancellationToken = default);
    Task RestoreFileAsync(string remoteFileId, CancellationToken cancellationToken = default);
    Task DeletePermanentlyAsync(string remoteFileId, CancellationToken cancellationToken = default);
    Task EmptyTrashAsync(CancellationToken cancellationToken = default);
    Task<bool> TrySendGmailAlertAsync(string recipientEmail, string subject, string htmlBody, string? accountKey = null, CancellationToken cancellationToken = default);
}
