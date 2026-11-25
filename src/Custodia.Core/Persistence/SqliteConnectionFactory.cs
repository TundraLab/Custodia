using Microsoft.Data.Sqlite;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Security;

namespace TundraDrive.Core.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteConnectionFactory()
        : this(AppPaths.DatabaseFilePath)
    {
    }

    public SqliteConnectionFactory(string databasePath)
    {
        _databasePath = databasePath;
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            PosixPermissionHelper.EnsurePrivateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);

        PosixPermissionHelper.EnsureFileMode0600(_databasePath);
        return connection;
    }

    public static void ClearAllConnectionPools() => SqliteConnection.ClearAllPools();
}
