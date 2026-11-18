using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Models;

namespace TundraDrive.Core.Sync;

public sealed class LocalFolderWatcherService : IDisposable
{
    private readonly IAppSettingsService _settingsService;
    private readonly ILogger<LocalFolderWatcherService> _logger;
    private readonly ConcurrentDictionary<string, WatcherEntry> _watchers = new();
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(1500);

    public event Func<string, Task>? FolderChangeDetected;

    public LocalFolderWatcherService(
        IAppSettingsService settingsService,
        ILogger<LocalFolderWatcherService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public void UpdateWatchers(IEnumerable<LocalSyncFolder> folders)
    {
        var desired = _settingsService.Current.WatchLocalChanges
            ? folders
                .Where(f => !f.IsPaused && Directory.Exists(f.LocalPath))
                .ToDictionary(f => f.Id, f => f)
            : new Dictionary<string, LocalSyncFolder>(StringComparer.Ordinal);

        foreach (var existingId in _watchers.Keys.ToList())
        {
            if (!desired.ContainsKey(existingId) && _watchers.TryRemove(existingId, out var removed))
            {
                removed.Dispose();
            }
        }

        if (desired.Count == 0)
        {
            return;
        }

        foreach (var (folderId, folder) in desired)
        {
            if (_watchers.TryGetValue(folderId, out var existing))
            {
                if (string.Equals(existing.LocalPath, folder.LocalPath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_watchers.TryRemove(folderId, out var stale))
                {
                    stale.Dispose();
                }
            }

            try
            {
                var fsw = new FileSystemWatcher(folder.LocalPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                var entry = new WatcherEntry(folderId, folder.LocalPath, fsw);
                fsw.Changed += (_, e) => OnFsEvent(entry, e.FullPath);
                fsw.Created += (_, e) => OnFsEvent(entry, e.FullPath);
                fsw.Deleted += (_, e) => OnFsEvent(entry, e.FullPath);
                fsw.Renamed += (_, e) => OnFsEvent(entry, e.FullPath);
                fsw.Error += (_, e) =>
                    _logger.LogWarning(e.GetException(), "FileSystemWatcher error on '{Path}'.", folder.LocalPath);

                _watchers[folderId] = entry;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not start FileSystemWatcher on '{Path}'.", folder.LocalPath);
            }
        }
    }

    private void OnFsEvent(WatcherEntry entry, string fullPath)
    {
        if (!_settingsService.Current.WatchLocalChanges)
        {
            return;
        }

        var relative = Path.GetRelativePath(entry.LocalPath, fullPath).Replace('\\', '/');
        if (SyncIgnoreMatcher.IsIgnored(relative, _settingsService.Current.IgnorePatterns))
        {
            return;
        }

        entry.ScheduleDebounced(_debounceDelay, async () =>
        {
            if (FolderChangeDetected is not null)
            {
                try
                {
                    await FolderChangeDetected.Invoke(entry.FolderId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling debounced change for folder '{FolderId}'.", entry.FolderId);
                }
            }
        });
    }

    public void Dispose()
    {
        foreach (var entry in _watchers.Values)
        {
            entry.Dispose();
        }
        _watchers.Clear();
    }

    private sealed class WatcherEntry : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly object _timerLock = new();
        private CancellationTokenSource? _debounceCts;

        public string FolderId { get; }
        public string LocalPath { get; }

        public WatcherEntry(string folderId, string localPath, FileSystemWatcher watcher)
        {
            FolderId = folderId;
            LocalPath = localPath;
            _watcher = watcher;
        }

        public void ScheduleDebounced(TimeSpan delay, Func<Task> callback)
        {
            lock (_timerLock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();
                var token = _debounceCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delay, token);
                        if (!token.IsCancellationRequested)
                        {
                            await callback();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }, CancellationToken.None);
            }
        }

        public void Dispose()
        {
            lock (_timerLock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = null;
            }
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
