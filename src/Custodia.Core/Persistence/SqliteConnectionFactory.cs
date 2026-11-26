using Microsoft.Data.Sqlite;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Security;

namespace TundraDrive.Core.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _walGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _walInitialized;

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
            Cache = SqliteCacheMode.Default
        }.ToString();
    }

    public async Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable WAL once per factory to avoid write-lock contention on concurrent connection opens.
        if (Volatile.Read(ref _walInitialized) == 0)
        {
            await _walGate.WaitAsync(cancellationToken);
            try
            {
                if (Volatile.Read(ref _walInitialized) == 0)
                {
                    await using var walCmd = connection.CreateCommand();
                    walCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                    await walCmd.ExecuteNonQueryAsync(cancellationToken);
                    Volatile.Write(ref _walInitialized, 1);
                }
            }
            finally
            {
                _walGate.Release();
            }
        }

        await using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;";
        await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);

        PosixPermissionHelper.EnsureFileMode0600(_databasePath);
        return connection;
    }

    internal async Task<IDisposable> AcquireWriteLockAsync(CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        return new Releaser(_writeGate);
    }

    public static void ClearAllConnectionPools() => SqliteConnection.ClearAllPools();

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
