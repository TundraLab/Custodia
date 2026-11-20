using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Persistence;

namespace TundraDrive.Desktop.Services;

public sealed record MemoryCleanupResult(
    long BeforeBytes,
    long AfterBytes,
    long FreedBytes);

/// <summary>
/// Periodic and on-demand RAM cleaner that drops offscreen media caches,
/// clears pooled SQLite handles, compacts the managed LOH/heap, and trims glibc arenas on Linux.
/// </summary>
public sealed class MemoryCleanerService : IDisposable
{
    private readonly IAppSettingsService _settingsService;
    private readonly ILogger<MemoryCleanerService> _logger;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private CancellationTokenSource? _loopCts;
    private CancellationTokenSource? _waitInterruptCts;
    private Task? _loopTask;
    private bool _disposed;

    public MemoryCleanerService(
        IAppSettingsService settingsService,
        ILogger<MemoryCleanerService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public Func<bool, Task>? ReleaseUiCachesCallback { get; set; }

    public DateTimeOffset? LastCleanedAtUtc { get; private set; }
    public long LastFreedBytes { get; private set; }
    public long LastRssBytes { get; private set; }

    public event EventHandler? CleanupCompleted;

    public void Start()
    {
        if (_disposed || _loopTask is not null)
        {
            return;
        }

        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunPeriodicLoopAsync(_loopCts.Token));
    }

    public void NotifySettingsUpdated()
    {
        try
        {
            _waitInterruptCts?.Cancel();
        }
        catch
        {
        }
    }

    private async Task RunPeriodicLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var enabled = _settingsService.Current.AutoMemoryCleanupEnabled;
            var minutes = Math.Clamp(_settingsService.Current.MemoryCleanupIntervalMinutes, 1, 60);
            var delay = enabled ? TimeSpan.FromMinutes(minutes) : TimeSpan.FromMinutes(1);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _waitInterruptCts = linkedCts;

            try
            {
                await Task.Delay(delay, linkedCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            finally
            {
                _waitInterruptCts = null;
            }

            if (ct.IsCancellationRequested || !_settingsService.Current.AutoMemoryCleanupEnabled)
            {
                continue;
            }

            try
            {
                await CleanNowAsync(forceReleaseMedia: false, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Periodic RAM cleanup iteration failed.");
            }
        }
    }

    public async Task<MemoryCleanupResult> CleanNowAsync(
        bool forceReleaseMedia = false,
        CancellationToken cancellationToken = default)
    {
        await _cleanupLock.WaitAsync(cancellationToken);
        try
        {
            var before = GetCurrentProcessPrivateMemoryBytes();

            if (ReleaseUiCachesCallback is not null)
            {
                try
                {
                    await ReleaseUiCachesCallback.Invoke(forceReleaseMedia);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "UI cache release callback failed during RAM cleanup.");
                }
            }

            await Task.Run(() =>
            {
                SqliteConnectionFactory.ClearAllConnectionPools();

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

                if (OperatingSystem.IsLinux())
                {
                    try
                    {
                        _ = malloc_trim(0);
                    }
                    catch
                    {
                        // Ignore if libc malloc_trim is unavailable.
                    }
                }
            }, cancellationToken);

            var after = GetCurrentProcessPrivateMemoryBytes();
            var freed = Math.Max(0, before - after);

            LastCleanedAtUtc = DateTimeOffset.UtcNow;
            LastFreedBytes = freed;
            LastRssBytes = after;

            _logger.LogInformation(
                "RAM cleanup finished: Private RAM {BeforeMb:F1} MB -> {AfterMb:F1} MB (freed {FreedMb:F1} MB).",
                before / 1_000_000.0,
                after / 1_000_000.0,
                freed / 1_000_000.0);

            CleanupCompleted?.Invoke(this, EventArgs.Empty);
            return new MemoryCleanupResult(before, after, freed);
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    /// <summary>
    /// Returns private resident memory matching Linux System Monitor (statm resident - shared).
    /// </summary>
    public static long GetCurrentProcessPrivateMemoryBytes()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var statm = File.ReadAllText("/proc/self/statm");
                var parts = statm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 &&
                    long.TryParse(parts[1], out var residentPages) &&
                    long.TryParse(parts[2], out var sharedPages))
                {
                    var privatePages = Math.Max(0, residentPages - sharedPages);
                    return privatePages * Environment.SystemPageSize;
                }
            }
            catch
            {
                // Fallback below.
            }
        }

        try
        {
            using var proc = Process.GetCurrentProcess();
            return proc.WorkingSet64;
        }
        catch
        {
            return GC.GetTotalMemory(false);
        }
    }

    [DllImport("libc.so.6", EntryPoint = "malloc_trim")]
    private static extern int malloc_trim(nuint pad);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _loopCts?.Cancel();
            _waitInterruptCts?.Cancel();
        }
        catch
        {
        }

        _loopCts?.Dispose();
        _cleanupLock.Dispose();
    }
}
