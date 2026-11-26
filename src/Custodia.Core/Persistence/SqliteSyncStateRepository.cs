using Dapper;
using TundraDrive.Core.Models;

namespace TundraDrive.Core.Persistence;

public sealed class SqliteSyncStateRepository : ISyncStateRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSyncStateRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DriveAccount?> GetAccountAsync(
        string accountId = "default",
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT
                id AS Id,
                display_name AS DisplayName,
                email_address AS EmailAddress,
                photo_url AS PhotoUrl,
                is_connected AS IsConnected,
                connected_at_utc AS ConnectedAtUtc,
                quota_limit_bytes AS QuotaLimitBytes,
                quota_usage_bytes AS QuotaUsageBytes,
                quota_drive_bytes AS QuotaDriveBytes,
                quota_trash_bytes AS QuotaTrashBytes
            FROM drive_accounts
            WHERE id = @AccountId;
            """;

        var row = await connection.QuerySingleOrDefaultAsync<AccountRow>(
            new CommandDefinition(sql, new { AccountId = accountId }, cancellationToken: cancellationToken));

        return row is null ? null : MapAccountRow(row);
    }

    public async Task<IReadOnlyList<DriveAccount>> GetAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT
                id AS Id,
                display_name AS DisplayName,
                email_address AS EmailAddress,
                photo_url AS PhotoUrl,
                is_connected AS IsConnected,
                connected_at_utc AS ConnectedAtUtc,
                quota_limit_bytes AS QuotaLimitBytes,
                quota_usage_bytes AS QuotaUsageBytes,
                quota_drive_bytes AS QuotaDriveBytes,
                quota_trash_bytes AS QuotaTrashBytes
            FROM drive_accounts
            ORDER BY connected_at_utc DESC, email_address ASC;
            """;

        var rows = await connection.QueryAsync<AccountRow>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(MapAccountRow).ToList();
    }

    public async Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        using var writeLock = await _connectionFactory.AcquireWriteLockAsync(cancellationToken);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM drive_accounts WHERE id = @AccountId;",
            new { AccountId = accountId },
            cancellationToken: cancellationToken));
    }

    private static DriveAccount MapAccountRow(AccountRow row) => new()
    {
        Id = row.Id,
        DisplayName = row.DisplayName,
        EmailAddress = row.EmailAddress,
        PhotoUrl = row.PhotoUrl,
        IsConnected = row.IsConnected,
        ConnectedAtUtc = row.ConnectedAtUtc,
        Quota = new StorageQuotaInfo
        {
            LimitBytes = row.QuotaLimitBytes,
            UsageBytes = row.QuotaUsageBytes,
            UsageInDriveBytes = row.QuotaDriveBytes,
            UsageInTrashBytes = row.QuotaTrashBytes
        }
    };

    public async Task UpsertAccountAsync(DriveAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        using var writeLock = await _connectionFactory.AcquireWriteLockAsync(cancellationToken);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        const string sql = """
            INSERT INTO drive_accounts (
                id, display_name, email_address, photo_url, is_connected, connected_at_utc,
                quota_limit_bytes, quota_usage_bytes, quota_drive_bytes, quota_trash_bytes
            ) VALUES (
                @Id, @DisplayName, @EmailAddress, @PhotoUrl, @IsConnected, @ConnectedAtUtc,
                @QuotaLimitBytes, @QuotaUsageBytes, @QuotaDriveBytes, @QuotaTrashBytes
            )
            ON CONFLICT(id) DO UPDATE SET
                display_name = excluded.display_name,
                email_address = excluded.email_address,
                photo_url = excluded.photo_url,
                is_connected = excluded.is_connected,
                connected_at_utc = excluded.connected_at_utc,
                quota_limit_bytes = excluded.quota_limit_bytes,
                quota_usage_bytes = excluded.quota_usage_bytes,
                quota_drive_bytes = excluded.quota_drive_bytes,
                quota_trash_bytes = excluded.quota_trash_bytes;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            account.Id,
            account.DisplayName,
            account.EmailAddress,
            account.PhotoUrl,
            IsConnected = account.IsConnected ? 1 : 0,
            account.ConnectedAtUtc,
            QuotaLimitBytes = account.Quota.LimitBytes,
            QuotaUsageBytes = account.Quota.UsageBytes,
            QuotaDriveBytes = account.Quota.UsageInDriveBytes,
            QuotaTrashBytes = account.Quota.UsageInTrashBytes
        }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<LocalSyncFolder>> GetSyncFoldersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT
                f.id AS Id,
                f.account_id AS AccountId,
                f.account_email AS AccountEmail,
                f.local_path AS LocalPath,
                f.remote_folder_id AS RemoteFolderId,
                f.remote_folder_name AS RemoteFolderName,
                f.sync_mode AS Mode,
                f.sync_state AS State,
                f.is_paused AS IsPaused,
                f.last_synced_at_utc AS LastSyncedAtUtc,
                f.last_error_message AS LastErrorMessage,
                f.custom_alias AS CustomAlias,
                f.custom_color_hex AS CustomColorHex,
                f.custom_icon_key AS CustomIconKey,
                (SELECT COUNT(1) FROM tracked_file_entries t WHERE t.sync_folder_id = f.id AND t.is_folder = 0) AS TrackedFileCount
            FROM local_sync_folders f
            ORDER BY f.local_path ASC;
            """;

        var items = await connection.QueryAsync<LocalSyncFolder>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return items.AsList();
    }

    public async Task<IReadOnlyList<LocalSyncFolder>> GetSyncFoldersByAccountAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT
                f.id AS Id,
                f.account_id AS AccountId,
                f.account_email AS AccountEmail,
                f.local_path AS LocalPath,
                f.remote_folder_id AS RemoteFolderId,
                f.remote_folder_name AS RemoteFolderName,
                f.sync_mode AS Mode,
                f.sync_state AS State,
                f.is_paused AS IsPaused,
                f.last_synced_at_utc AS LastSyncedAtUtc,
                f.last_error_message AS LastErrorMessage,
                f.custom_alias AS CustomAlias,
                f.custom_color_hex AS CustomColorHex,
                f.custom_icon_key AS CustomIconKey,
                (SELECT COUNT(1) FROM tracked_file_entries t WHERE t.sync_folder_id = f.id AND t.is_folder = 0) AS TrackedFileCount
            FROM local_sync_folders f
            WHERE f.account_id = @AccountId
            ORDER BY f.local_path ASC;
            """;

        var items = await connection.QueryAsync<LocalSyncFolder>(
            new CommandDefinition(sql, new { AccountId = accountId }, cancellationToken: cancellationToken));
        return items.AsList();
    }

    public async Task<LocalSyncFolder?> GetSyncFolderByIdAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT
                f.id AS Id,
                f.account_id AS AccountId,
                f.account_email AS AccountEmail,
                f.local_path AS LocalPath,
                f.remote_folder_id AS RemoteFolderId,
                f.remote_folder_name AS RemoteFolderName,
                f.sync_mode AS Mode,
                f.sync_state AS State,
                f.is_paused AS IsPaused,
                f.last_synced_at_utc AS LastSyncedAtUtc,
                f.last_error_message AS LastErrorMessage,
                f.custom_alias AS CustomAlias,
                f.custom_color_hex AS CustomColorHex,
                f.custom_icon_key AS CustomIconKey,
                (SELECT COUNT(1) FROM tracked_file_entries t WHERE t.sync_folder_id = f.id AND t.is_folder = 0) AS TrackedFileCount
            FROM local_sync_folders f
            WHERE f.id = @FolderId;
            """;

        return await connection.QuerySingleOrDefaultAsync<LocalSyncFolder>(
            new CommandDefinition(sql, new { FolderId = folderId }, cancellationToken: cancellationToken));
    }

    public async Task UpsertSyncFolderAsync(LocalSyncFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        using var writeLock = await _connectionFactory.AcquireWriteLockAsync(cancellationToken);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        const string findExistingSql = "SELECT id FROM local_sync_folders WHERE local_path = @LocalPath LIMIT 1;";
        var existingId = await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(findExistingSql, new { folder.LocalPath }, cancellationToken: cancellationToken));

        var effectiveId = !string.IsNullOrWhiteSpace(existingId) ? existingId : folder.Id;
        folder.Id = effectiveId;
        var normalizedEmail = string.IsNullOrWhiteSpace(folder.AccountEmail)
            ? null
            : folder.AccountEmail.Trim().ToLowerInvariant();

        const string sql = """
            INSERT INTO local_sync_folders (
                id, account_id, account_email, local_path, remote_folder_id, remote_folder_name,
                sync_mode, sync_state, is_paused, last_synced_at_utc, last_error_message,
                custom_alias, custom_color_hex, custom_icon_key
            ) VALUES (
                @Id, @AccountId, @AccountEmail, @LocalPath, @RemoteFolderId, @RemoteFolderName,
                @Mode, @State, @IsPaused, @LastSyncedAtUtc, @LastErrorMessage,
                @CustomAlias, @CustomColorHex, @CustomIconKey
            )
            ON CONFLICT(id) DO UPDATE SET
                account_id = excluded.account_id,
                account_email = COALESCE(excluded.account_email, local_sync_folders.account_email),
                local_path = excluded.local_path,
                remote_folder_id = excluded.remote_folder_id,
                remote_folder_name = excluded.remote_folder_name,
                sync_mode = excluded.sync_mode,
                sync_state = excluded.sync_state,
                is_paused = excluded.is_paused,
                last_synced_at_utc = excluded.last_synced_at_utc,
                last_error_message = excluded.last_error_message,
                custom_alias = excluded.custom_alias,
                custom_color_hex = excluded.custom_color_hex,
                custom_icon_key = excluded.custom_icon_key;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = effectiveId,
            folder.AccountId,
            AccountEmail = normalizedEmail,
            folder.LocalPath,
            folder.RemoteFolderId,
            folder.RemoteFolderName,
            Mode = (int)folder.Mode,
            State = (int)folder.State,
            IsPaused = folder.IsPaused ? 1 : 0,
            folder.LastSyncedAtUtc,
            folder.LastErrorMessage,
            folder.CustomAlias,
            folder.CustomColorHex,
            folder.CustomIconKey
        }, cancellationToken: cancellationToken));
    }

    public async Task<int> ReattachSyncFoldersByEmailAsync(
        string accountId,
        string accountEmail,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(accountEmail))
        {
            return 0;
        }

        using var writeLock = await _connectionFactory.AcquireWriteLockAsync(cancellationToken);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var normalizedEmail = accountEmail.Trim().ToLowerInvariant();
        const string sql = """
            UPDATE local_sync_folders
            SET account_id = @AccountId,
                account_email = @AccountEmail
            WHERE LOWER(account_email) = @AccountEmail
              AND account_id <> @AccountId;
            """;

        return await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { AccountId = accountId, AccountEmail = normalizedEmail },
            cancellationToken: cancellationToken));
    }

    public async Task DeleteSyncFolderAsync(string folderId, CancellationToken cancellationToken = default)
    {
        using var writeLock = await _connectionFactory.AcquireWriteLockAsync(cancellationToken);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = "DELETE FROM local_sync_folders WHERE id = @FolderId;";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { FolderId = folderId }, cancellationToken: cancellationToken));
    }

    public async Task DeleteSyncFoldersByAccountAsync(
        string accountId,
        string? accountEmail = null,
        CancellationToken cancellationToken = default)
    {
        using var writeLock = await _connectionFactory.AcquireWriteLockAsync(cancellationToken);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(accountEmail))
        {
            var normalizedEmail = accountEmail.Trim().ToLowerInvariant();
            const string sqlWithEmail = """
                DELETE FROM local_sync_folders
                WHERE account_id = @AccountId
                   OR LOWER(account_email) = @AccountEmail;
                """;
            await connection.ExecuteAsync(new CommandDefinition(
                sqlWithEmail,
                new { AccountId = accountId, AccountEmail = normalizedEmail },
                cancellationToken: cancellationToken));
            return;
        }

        const string sqlById = "DELETE FROM local_sync_folders WHERE account_id = @AccountId;";
        await connection.ExecuteAsync(new CommandDefinition(sqlById, new { AccountId = accountId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TrackedFileEntry>> GetTrackedFilesByFolderAsync(
        string syncFolderId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT
                id AS Id,
                sync_folder_id AS SyncFolderId,
                relative_path AS RelativePath,
                remote_file_id AS RemoteFileId,
                remote_parent_id AS RemoteParentId,
                is_folder AS IsFolder,
                md5_hash AS Md5Hash,
                sha256_hash AS Sha256Hash,
                size_bytes AS SizeBytes,
                local_modified_at_utc AS LocalModifiedAtUtc,
                remote_modified_at_utc AS RemoteModifiedAtUtc,
                last_synced_at_utc AS LastSyncedAtUtc
            FROM tracked_file_entries
            WHERE sync_folder_id = @SyncFolderId;
            """;

        var items = await connection.QueryAsync<TrackedFileEntry>(
            new CommandDefinition(sql, new { SyncFolderId = syncFolderId }, cancellationToken: cancellationToken));
        return items.AsList();
    }

    public async Task<TrackedFileEntry?> GetTrackedFileAsync(
        string syncFolderId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT
                id AS Id,
                sync_folder_id AS SyncFolderId,
                relative_path AS RelativePath,
                remote_file_id AS RemoteFileId,
                remote_parent_id AS RemoteParentId,
                is_folder AS IsFolder,
                md5_hash AS Md5Hash,
                sha256_hash AS Sha256Hash,
                size_bytes AS SizeBytes,
                local_modified_at_utc AS LocalModifiedAtUtc,
                remote_modified_at_utc AS RemoteModifiedAtUtc,
                last_synced_at_utc AS LastSyncedAtUtc
            FROM tracked_file_entries
            WHERE sync_folder_id = @SyncFolderId AND relative_path = @RelativePath;
            """;

        return await connection.QuerySingleOrDefaultAsync<TrackedFileEntry>(
            new CommandDefinition(sql, new { SyncFolderId = syncFolderId, RelativePath = relativePath }, cancellationToken: cancellationToken));
    }

    public async Task<TrackedFileEntry?> GetTrackedFileByRemoteIdAsync(
        string syncFolderId,
        string remoteFileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT
                id AS Id,
                sync_folder_id AS SyncFolderId,
                relative_path AS RelativePath,
                remote_file_id AS RemoteFileId,
                remote_parent_id AS RemoteParentId,
                is_folder AS IsFolder,
                md5_hash AS Md5Hash,
                sha256_hash AS Sha256Hash,
                size_bytes AS SizeBytes,
                local_modified_at_utc AS LocalModifiedAtUtc,
                remote_modified_at_utc AS RemoteModifiedAtUtc,
                last_synced_at_utc AS LastSyncedAtUtc
            FROM tracked_file_entries
            WHERE sync_folder_id = @SyncFolderId AND remote_file_id = @RemoteFileId
            LIMIT 1;
            """;

        return await connection.QuerySingleOrDefaultAsync<TrackedFileEntry>(
            new CommandDefinition(sql, new { SyncFolderId = syncFolderId, RemoteFileId = remoteFileId }, cancellationToken: cancellationToken));
    }

    public async Task UpsertTrackedFileAsync(TrackedFileEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var writeLock = await _connectionFactory.AcquireWriteLockAsync(cancellationToken);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        const string sql = """
            INSERT INTO tracked_file_entries (
                id, sync_folder_id, relative_path, remote_file_id, remote_parent_id,
                is_folder, md5_hash, sha256_hash, size_bytes,
                local_modified_at_utc, remote_modified_at_utc, last_synced_at_utc
            ) VALUES (
                @Id, @SyncFolderId, @RelativePath, @RemoteFileId, @RemoteParentId,
                @IsFolder, @Md5Hash, @Sha256Hash, @SizeBytes,
                @LocalModifiedAtUtc, @RemoteModifiedAtUtc, @LastSyncedAtUtc
            )
            ON CONFLICT(sync_folder_id, relative_path) DO UPDATE SET
                remote_file_id = excluded.remote_file_id,
                remote_parent_id = excluded.remote_parent_id,
                is_folder = excluded.is_folder,
                md5_hash = excluded.md5_hash,
                sha256_hash = excluded.sha256_hash,
                size_bytes = excluded.size_bytes,
                local_modified_at_utc = excluded.local_modified_at_utc,
                remote_modified_at_utc = excluded.remote_modified_at_utc,
                last_synced_at_utc = excluded.last_synced_at_utc;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            entry.Id,
            entry.SyncFolderId,
            entry.RelativePath,
            entry.RemoteFileId,
            entry.RemoteParentId,
            IsFolder = entry.IsFolder ? 1 : 0,
            entry.Md5Hash,
            entry.Sha256Hash,
            entry.SizeBytes,
            entry.LocalModifiedAtUtc,
            entry.RemoteModifiedAtUtc,
            entry.LastSyncedAtUtc
        }, cancellationToken: cancellationToken));
    }

    public async Task DeleteTrackedFileAsync(string entryId, CancellationToken cancellationToken = default)
    {
        using var writeLock = await _connectionFactory.AcquireWriteLockAsync(cancellationToken);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = "DELETE FROM tracked_file_entries WHERE id = @EntryId;";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { EntryId = entryId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TrackedFileEntry>> SearchTrackedFilesAsync(
        string query,
        string? syncFolderId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var pattern = $"%{query.Trim()}%";

        const string sql = """
            SELECT
                id AS Id,
                sync_folder_id AS SyncFolderId,
                relative_path AS RelativePath,
                remote_file_id AS RemoteFileId,
                remote_parent_id AS RemoteParentId,
                is_folder AS IsFolder,
                md5_hash AS Md5Hash,
                sha256_hash AS Sha256Hash,
                size_bytes AS SizeBytes,
                local_modified_at_utc AS LocalModifiedAtUtc,
                remote_modified_at_utc AS RemoteModifiedAtUtc,
                last_synced_at_utc AS LastSyncedAtUtc
            FROM tracked_file_entries
            WHERE (@SyncFolderId IS NULL OR sync_folder_id = @SyncFolderId)
              AND relative_path LIKE @Pattern
            ORDER BY last_synced_at_utc DESC
            LIMIT @Limit;
            """;

        var items = await connection.QueryAsync<TrackedFileEntry>(
            new CommandDefinition(sql, new { SyncFolderId = syncFolderId, Pattern = pattern, Limit = limit }, cancellationToken: cancellationToken));
        return items.AsList();
    }

    public async Task<long> AddActivityLogAsync(SyncActivityLog log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        using var writeLock = await _connectionFactory.AcquireWriteLockAsync(cancellationToken);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        const string sql = """
            INSERT INTO sync_activity_logs (
                sync_folder_id, severity, action, message, target_path, created_at_utc
            ) VALUES (
                @SyncFolderId, @Severity, @Action, @Message, @TargetPath, @CreatedAtUtc
            );
            SELECT last_insert_rowid();
            """;

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            log.SyncFolderId,
            Severity = (int)log.Severity,
            log.Action,
            log.Message,
            log.TargetPath,
            log.CreatedAtUtc
        }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SyncActivityLog>> GetActivityLogsAsync(
        ActivitySeverity? minSeverity = null,
        string? searchText = null,
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var pattern = string.IsNullOrWhiteSpace(searchText) ? null : $"%{searchText.Trim()}%";

        const string sql = """
            SELECT
                id AS Id,
                sync_folder_id AS SyncFolderId,
                severity AS Severity,
                action AS Action,
                message AS Message,
                target_path AS TargetPath,
                created_at_utc AS CreatedAtUtc
            FROM sync_activity_logs
            WHERE (@MinSeverity IS NULL OR severity >= @MinSeverity)
              AND (@Pattern IS NULL OR message LIKE @Pattern OR action LIKE @Pattern OR target_path LIKE @Pattern)
            ORDER BY id DESC
            LIMIT @Limit;
            """;

        var items = await connection.QueryAsync<SyncActivityLog>(new CommandDefinition(sql, new
        {
            MinSeverity = minSeverity.HasValue ? (int?)minSeverity.Value : null,
            Pattern = pattern,
            Limit = limit
        }, cancellationToken: cancellationToken));

        return items.AsList();
    }

    public async Task ClearActivityLogsAsync(CancellationToken cancellationToken = default)
    {
        using var writeLock = await _connectionFactory.AcquireWriteLockAsync(cancellationToken);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM sync_activity_logs;", cancellationToken: cancellationToken));
    }

    private sealed class AccountRow
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public string? PhotoUrl { get; set; }
        public bool IsConnected { get; set; }
        public DateTimeOffset? ConnectedAtUtc { get; set; }
        public long QuotaLimitBytes { get; set; }
        public long QuotaUsageBytes { get; set; }
        public long QuotaDriveBytes { get; set; }
        public long QuotaTrashBytes { get; set; }
    }
}
