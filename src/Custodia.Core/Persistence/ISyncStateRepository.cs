using TundraDrive.Core.Models;

namespace TundraDrive.Core.Persistence;

public interface ISyncStateRepository
{
    Task<DriveAccount?> GetAccountAsync(string accountId = "default", CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DriveAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default);
    Task UpsertAccountAsync(DriveAccount account, CancellationToken cancellationToken = default);
    Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalSyncFolder>> GetSyncFoldersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalSyncFolder>> GetSyncFoldersByAccountAsync(string accountId, CancellationToken cancellationToken = default);
    Task<LocalSyncFolder?> GetSyncFolderByIdAsync(string folderId, CancellationToken cancellationToken = default);
    Task UpsertSyncFolderAsync(LocalSyncFolder folder, CancellationToken cancellationToken = default);
    Task<int> ReattachSyncFoldersByEmailAsync(string accountId, string accountEmail, CancellationToken cancellationToken = default);
    Task DeleteSyncFolderAsync(string folderId, CancellationToken cancellationToken = default);
    Task DeleteSyncFoldersByAccountAsync(string accountId, string? accountEmail = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackedFileEntry>> GetTrackedFilesByFolderAsync(string syncFolderId, CancellationToken cancellationToken = default);
    Task<TrackedFileEntry?> GetTrackedFileAsync(string syncFolderId, string relativePath, CancellationToken cancellationToken = default);
    Task<TrackedFileEntry?> GetTrackedFileByRemoteIdAsync(string syncFolderId, string remoteFileId, CancellationToken cancellationToken = default);
    Task UpsertTrackedFileAsync(TrackedFileEntry entry, CancellationToken cancellationToken = default);
    Task DeleteTrackedFileAsync(string entryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrackedFileEntry>> SearchTrackedFilesAsync(string query, string? syncFolderId = null, int limit = 50, CancellationToken cancellationToken = default);

    Task<long> AddActivityLogAsync(SyncActivityLog log, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncActivityLog>> GetActivityLogsAsync(ActivitySeverity? minSeverity = null, string? searchText = null, int limit = 250, CancellationToken cancellationToken = default);
    Task ClearActivityLogsAsync(CancellationToken cancellationToken = default);
}
