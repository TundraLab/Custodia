using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace TundraDrive.Core.Configuration;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string ActivateCommand = "ACTIVATE";
    public const string QuitCommand = "QUIT";

    private readonly string _lockFilePath;
    private readonly string _socketPath;
    private readonly FileStream _lockStream;
    private readonly Socket? _serverSocket;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoopTask;
    private int _disposed;
    private bool _pendingActivation;
    private bool _pendingShutdown;
    private EventHandler? _activationRequested;
    private EventHandler? _shutdownRequested;

    public event EventHandler? ShutdownRequested
    {
        add
        {
            var invokeImmediately = false;
            lock (_syncLock)
            {
                _shutdownRequested += value;
                if (_pendingShutdown && value is not null)
                {
                    _pendingShutdown = false;
                    invokeImmediately = true;
                }
            }

            if (invokeImmediately)
            {
                value?.Invoke(this, EventArgs.Empty);
            }
        }
        remove
        {
            lock (_syncLock)
            {
                _shutdownRequested -= value;
            }
        }
    }

    public event EventHandler? ActivationRequested
    {
        add
        {
            var invokeImmediately = false;
            lock (_syncLock)
            {
                _activationRequested += value;
                if (_pendingActivation && value is not null)
                {
                    _pendingActivation = false;
                    invokeImmediately = true;
                }
            }

            if (invokeImmediately)
            {
                value?.Invoke(this, EventArgs.Empty);
            }
        }
        remove
        {
            lock (_syncLock)
            {
                _activationRequested -= value;
            }
        }
    }

    private readonly object _syncLock = new();

    private SingleInstanceGuard(
        string lockFilePath,
        string socketPath,
        FileStream lockStream,
        Socket? serverSocket)
    {
        _lockFilePath = lockFilePath;
        _socketPath = socketPath;
        _lockStream = lockStream;
        _serverSocket = serverSocket;

        if (_serverSocket is not null)
        {
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }
    }

    public static string DefaultLockFilePath =>
        Path.Combine(AppPaths.DataDirectory, "tundradrive.lock");

    public static string ResolveDefaultSocketPath()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtimeDir) && Directory.Exists(runtimeDir))
        {
            var candidate = Path.Combine(runtimeDir, "tundradrive.sock");
            if (Encoding.UTF8.GetByteCount(candidate) < 100)
            {
                return candidate;
            }
        }

        var uid = OperatingSystem.IsLinux() ? GetUidSafe() : (uint)(Environment.UserName.GetHashCode() & 0x7fffffff);
        return Path.Combine(Path.GetTempPath(), $"tundradrive-{uid}.sock");
    }

    public static SingleInstanceAcquireResult TryAcquireOrActivate(
        string? lockFilePath = null,
        string? socketPath = null,
        TimeSpan? activateTimeout = null)
    {
        var effectiveLockPath = string.IsNullOrWhiteSpace(lockFilePath)
            ? DefaultLockFilePath
            : lockFilePath;
        var effectiveSocketPath = string.IsNullOrWhiteSpace(socketPath)
            ? ResolveDefaultSocketPath()
            : socketPath;
        var timeout = activateTimeout ?? TimeSpan.FromMilliseconds(1500);

        var lockDir = Path.GetDirectoryName(effectiveLockPath);
        if (!string.IsNullOrWhiteSpace(lockDir))
        {
            Directory.CreateDirectory(lockDir);
        }

        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(
                effectiveLockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            var signaled = TrySignalExistingInstance(effectiveSocketPath, timeout);
            return new SingleInstanceAcquireResult(false, null, signaled);
        }
        catch (UnauthorizedAccessException)
        {
            var signaled = TrySignalExistingInstance(effectiveSocketPath, timeout);
            return new SingleInstanceAcquireResult(false, null, signaled);
        }

        Socket? serverSocket = null;
        try
        {
            var socketDir = Path.GetDirectoryName(effectiveSocketPath);
            if (!string.IsNullOrWhiteSpace(socketDir))
            {
                Directory.CreateDirectory(socketDir);
            }

            if (File.Exists(effectiveSocketPath))
            {
                File.Delete(effectiveSocketPath);
            }

            serverSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            serverSocket.Bind(new UnixDomainSocketEndPoint(effectiveSocketPath));
            serverSocket.Listen(8);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    File.SetUnixFileMode(
                        effectiveSocketPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch
                {
                    // Best-effort socket permissions.
                }
            }
        }
        catch
        {
            serverSocket?.Dispose();
            serverSocket = null;
        }

        var guard = new SingleInstanceGuard(effectiveLockPath, effectiveSocketPath, lockStream, serverSocket);
        return new SingleInstanceAcquireResult(true, guard, false);
    }

    public static bool TryRequestShutdownExisting(string? socketPath = null, TimeSpan? timeout = null) =>
        TrySignalExistingInstance(
            string.IsNullOrWhiteSpace(socketPath) ? ResolveDefaultSocketPath() : socketPath,
            timeout ?? TimeSpan.FromMilliseconds(800),
            QuitCommand);

    private static bool TrySignalExistingInstance(
        string socketPath,
        TimeSpan timeout,
        string command = ActivateCommand)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (File.Exists(socketPath))
            {
                try
                {
                    using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    client.Connect(new UnixDomainSocketEndPoint(socketPath));

                    var payload = Encoding.UTF8.GetBytes(command + "\n");
                    client.Send(payload);

                    client.ReceiveTimeout = 800;
                    var ack = new byte[16];
                    var received = client.Receive(ack);
                    if (received > 0)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Primary may still be starting up or shutting down.
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            Thread.Sleep(75);
        }
        while (true);

        return false;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        if (_serverSocket is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = await _serverSocket.AcceptAsync(cancellationToken);
                var buffer = new byte[128];
                var bytesRead = await client.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
                var message = bytesRead > 0
                    ? Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim()
                    : string.Empty;

                var ack = Encoding.UTF8.GetBytes("OK\n");
                await client.SendAsync(ack, SocketFlags.None, cancellationToken);

                if (message.StartsWith(QuitCommand, StringComparison.OrdinalIgnoreCase))
                {
                    RaiseShutdownRequested();
                }
                else if (string.IsNullOrEmpty(message) ||
                    message.StartsWith(ActivateCommand, StringComparison.OrdinalIgnoreCase))
                {
                    RaiseActivationRequested();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private void RaiseActivationRequested()
    {
        EventHandler? handler;
        lock (_syncLock)
        {
            handler = _activationRequested;
            if (handler is null)
            {
                _pendingActivation = true;
                return;
            }
        }

        handler.Invoke(this, EventArgs.Empty);
    }

    private void RaiseShutdownRequested()
    {
        EventHandler? handler;
        lock (_syncLock)
        {
            handler = _shutdownRequested;
            if (handler is null)
            {
                _pendingShutdown = true;
                return;
            }
        }

        handler.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            _serverSocket?.Dispose();
        }
        catch
        {
        }

        try
        {
            _acceptLoopTask?.Wait(500);
        }
        catch
        {
        }

        try
        {
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }
        }
        catch
        {
        }

        try
        {
            _lockStream.Dispose();
        }
        catch
        {
        }

        try
        {
            if (File.Exists(_lockFilePath))
            {
                File.Delete(_lockFilePath);
            }
        }
        catch
        {
        }

        _cts.Dispose();
    }

    private static uint GetUidSafe()
    {
        try
        {
            return geteuid();
        }
        catch
        {
            return 1000;
        }
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint geteuid();
}

public sealed record SingleInstanceAcquireResult(
    bool IsPrimary,
    SingleInstanceGuard? Guard,
    bool SignaledExistingInstance) : IDisposable
{
    public void Dispose() => Guard?.Dispose();
}
