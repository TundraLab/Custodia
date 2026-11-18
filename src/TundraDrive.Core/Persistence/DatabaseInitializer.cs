using System.Data;
using System.Globalization;
using Dapper;

namespace TundraDrive.Core.Persistence;

internal sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        parameter.Value = value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    public override DateTimeOffset Parse(object value)
    {
        if (value is DateTimeOffset dto)
        {
            return dto;
        }

        return DateTimeOffset.Parse(
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }
}

internal sealed class NullableDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset?>
{
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset? value)
    {
        parameter.Value = value.HasValue
            ? value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : DBNull.Value;
    }

    public override DateTimeOffset? Parse(object value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is DateTimeOffset dto)
        {
            return dto;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}

public static class DatabaseInitializer
{
    private static int _handlersRegistered;

    public static async Task InitializeAsync(
        SqliteConnectionFactory connectionFactory,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _handlersRegistered, 1) == 0)
        {
            SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
            SqlMapper.AddTypeHandler(new NullableDateTimeOffsetHandler());
        }

        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        const string schemaSql = """
            CREATE TABLE IF NOT EXISTS drive_accounts (
                id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                email_address TEXT NOT NULL,
                photo_url TEXT NULL,
                is_connected INTEGER NOT NULL DEFAULT 0,
                connected_at_utc TEXT NULL,
                quota_limit_bytes INTEGER NOT NULL DEFAULT 0,
                quota_usage_bytes INTEGER NOT NULL DEFAULT 0,
                quota_drive_bytes INTEGER NOT NULL DEFAULT 0,
                quota_trash_bytes INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS local_sync_folders (
                id TEXT PRIMARY KEY,
                account_id TEXT NOT NULL,
                local_path TEXT NOT NULL UNIQUE,
                remote_folder_id TEXT NOT NULL,
                remote_folder_name TEXT NOT NULL,
                sync_mode INTEGER NOT NULL DEFAULT 0,
                sync_state INTEGER NOT NULL DEFAULT 0,
                is_paused INTEGER NOT NULL DEFAULT 0,
                last_synced_at_utc TEXT NULL,
                last_error_message TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS tracked_file_entries (
                id TEXT PRIMARY KEY,
                sync_folder_id TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                remote_file_id TEXT NOT NULL,
                remote_parent_id TEXT NOT NULL,
                is_folder INTEGER NOT NULL DEFAULT 0,
                md5_hash TEXT NULL,
                sha256_hash TEXT NULL,
                size_bytes INTEGER NOT NULL DEFAULT 0,
                local_modified_at_utc TEXT NOT NULL,
                remote_modified_at_utc TEXT NOT NULL,
                last_synced_at_utc TEXT NOT NULL,
                FOREIGN KEY (sync_folder_id) REFERENCES local_sync_folders(id) ON DELETE CASCADE,
                UNIQUE (sync_folder_id, relative_path)
            );

            CREATE INDEX IF NOT EXISTS idx_tracked_files_folder
                ON tracked_file_entries (sync_folder_id);

            CREATE INDEX IF NOT EXISTS idx_tracked_files_remote_id
                ON tracked_file_entries (remote_file_id);

            CREATE INDEX IF NOT EXISTS idx_tracked_files_path
                ON tracked_file_entries (relative_path);

            CREATE TABLE IF NOT EXISTS sync_activity_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sync_folder_id TEXT NULL,
                severity INTEGER NOT NULL DEFAULT 0,
                action TEXT NOT NULL,
                message TEXT NOT NULL,
                target_path TEXT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_activity_logs_created
                ON sync_activity_logs (created_at_utc DESC);
            """;

        await connection.ExecuteAsync(new CommandDefinition(schemaSql, cancellationToken: cancellationToken));
    }
}
