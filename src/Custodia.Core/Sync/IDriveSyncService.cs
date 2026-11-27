using TundraDrive.Core.Models;

namespace TundraDrive.Core.Sync;

public interface IDriveSyncService
{
    DriveAccount CurrentAccount { get; }
    IReadOnlyList<DriveAccount> SavedAccounts { get; }
    bool IsSyncRunning { get; }
    bool IsOnline { get; }

    event EventHandler<DriveAccount>? AccountChanged;
    event EventHandler? AccountsListChanged;
    event EventHandler? SyncFoldersChanged;
    event EventHandler<TransferQueueItem>? TransferUpdated;
    event EventHandler<SyncActivityLog>? ActivityLogged;
    event EventHandler<bool>? ConnectivityChanged;

    Task<bool> ProbeConnectivityAsync(CancellationToken cancellationToken = default);
    void ReportNetworkFailure();

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<DriveAccount> ConnectAccountAsync(bool addNewAccount = false, string? customCredentialsJsonPath = null, CancellationToken cancellationToken = default);
    Task<DriveAccount> ReconnectAccountAsync(string accountId, string? customCredentialsJsonPath = null, CancellationToken cancellationToken = default);
    Task<DriveAccount?> SwitchAccountAsync(string accountId, CancellationToken cancellationToken = default);
    Task DisconnectAccountAsync(string? accountId = null, bool deleteFolderConfiguration = false, CancellationToken cancellationToken = default);
    Task RevokeCredentialsFileAsync(CancellationToken cancellationToken = default);
    Task RevokeAllAccountsAndCredentialsAsync(CancellationToken cancellationToken = default);
    Task<DriveAccount?> RefreshAccountInfoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalSyncFolder>> GetSyncFoldersAsync(CancellationToken cancellationToken = default);
    Task<LocalSyncFolder> AddSyncFolderAsync(string localPath, string remoteFolderId, string remoteFolderName, SyncMode mode, CancellationToken cancellationToken = default);
    Task UpdateSyncModeAsync(string folderId, SyncMode mode, CancellationToken cancellationToken = default);
    Task UpdateFolderCustomizationAsync(string folderId, string? customAlias, string? customColorHex, string? customIconKey, CancellationToken cancellationToken = default);
    Task SetFolderPausedAsync(string folderId, bool isPaused, CancellationToken cancellationToken = default);
    Task RemoveSyncFolderAsync(string folderId, CancellationToken cancellationToken = default);

    Task TriggerSyncAllAsync(CancellationToken cancellationToken = default);
    Task TriggerFolderSyncAsync(string folderId, CancellationToken cancellationToken = default);
    Task<RemoteDriveNode> UploadSingleFileAsync(string localFilePath, string remoteFolderId = "root", CancellationToken cancellationToken = default);
    Task<string> DownloadRemoteFileAsync(string remoteFileId, string fileName, long sizeBytes = 0, bool isGoogleWorkspaceDoc = false, string? mimeType = null, CancellationToken cancellationToken = default);
    Task<string> DownloadRemoteFileToAsync(string remoteFileId, string fileName, string destinationPathOrDirectory, bool treatAsFullFilePath = false, long sizeBytes = 0, bool isGoogleWorkspaceDoc = false, string? mimeType = null, CancellationToken cancellationToken = default) =>
        DownloadRemoteFileAsync(remoteFileId, fileName, sizeBytes, isGoogleWorkspaceDoc, mimeType, cancellationToken);
    Task TrashRemoteItemAsync(string remoteFileId, string itemName, CancellationToken cancellationToken = default);
    Task RestoreRemoteItemAsync(string remoteFileId, string itemName, CancellationToken cancellationToken = default);
    Task DeleteRemoteItemPermanentlyAsync(string remoteFileId, string itemName, CancellationToken cancellationToken = default);
    Task EmptyRemoteTrashAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DrivePermissionEntry>> GetRemotePermissionsAsync(string remoteFileId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DrivePermissionEntry>>(Array.Empty<DrivePermissionEntry>());
    Task<string> EnableAnyoneLinkAsync(string remoteFileId, string itemName, bool canEdit, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Empty);
    Task DisableAnyoneLinkAsync(string remoteFileId, string itemName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
    Task<DrivePermissionEntry> ShareWithEmailAsync(string remoteFileId, string itemName, string emailAddress, bool canEdit, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DrivePermissionEntry());
    Task RevokeRemotePermissionAsync(string remoteFileId, string itemName, string permissionId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    IReadOnlyList<TransferQueueItem> GetTransferQueueSnapshot();
    TransferQueueStats GetTransferQueueStats();
    void CancelTransfer(string transferId);
    void ClearCompletedTransfers();
    Task<bool> RetryTransferAsync(string transferId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
    Task<int> RetryAllFailedTransfersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
