using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using TundraDrive.Api;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;
using TundraDrive.Desktop.ViewModels;
using TundraDrive.Desktop.Views;

namespace TundraDrive.Tests;

public sealed class BatchConcurrencyAndFolderDeletionTests
{
    [Fact]
    public async Task SettingsViewModel_PersistsConcurrencyLimitToggleAndMaxUpToSix()
    {
        var settings = new InMemorySettingsService();
        var apiProxy = DispatchProxy.Create<IEmbeddedApiServer, BasicSettingsStubProxy>();
        var autostartProxy = DispatchProxy.Create<IAutostartManager, BasicSettingsStubProxy>();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var loc = new LocalizationService();
        var notify = new RecordingNotificationService();
        var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
        using var cleaner = new MemoryCleanerService(settings, NullLogger<MemoryCleanerService>.Instance);

        var vm = new SettingsViewModel(settings, apiProxy, autostartProxy, loc, notify, auth, syncProxy, cleaner);

        Assert.True(vm.LimitConcurrentTransfers);
        Assert.Equal(3m, vm.MaxConcurrentTransfers);
        Assert.Equal(3, settings.Current.GetEffectiveTransferConcurrencyLimit());

        vm.LimitConcurrentTransfers = true;
        vm.MaxConcurrentTransfers = 6m;
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.True(settings.Current.LimitConcurrentTransfers);
        Assert.Equal(6, settings.Current.MaxConcurrentTransfers);
        Assert.Equal(6, settings.Current.GetEffectiveTransferConcurrencyLimit());

        // Clamp values above 6
        vm.MaxConcurrentTransfers = 12m;
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Equal(6, settings.Current.MaxConcurrentTransfers);
        Assert.Equal(6, settings.Current.GetEffectiveTransferConcurrencyLimit());

        // Disable limit completely
        vm.LimitConcurrentTransfers = false;
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.False(settings.Current.LimitConcurrentTransfers);
        Assert.Null(settings.Current.GetEffectiveTransferConcurrencyLimit());
    }

    [Fact]
    public async Task FileHubViewModel_RespectsConfiguredConcurrencyLimitUpToSix_AndRunsUnlimitedWhenDisabled()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-concurrency-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var localFiles = new List<string>();
            for (var i = 0; i < 8; i++)
            {
                var file = Path.Combine(tempRoot, $"file-{i}.txt");
                await File.WriteAllTextAsync(file, $"content-{i}");
                localFiles.Add(file);
            }

            var loc = new LocalizationService();
            var settings = new InMemorySettingsService();
            var syncProxy = DispatchProxy.Create<IDriveSyncService, ConcurrencyTrackingSyncProxy>();
            var syncState = (ConcurrencyTrackingSyncProxy)(object)syncProxy;
            var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
            var repoProxy = DispatchProxy.Create<ISyncStateRepository, StubStateRepoProxy>();
            var hub = new FileHubViewModel(repoProxy, driveProxy, syncProxy, new FilePreviewService(driveProxy), loc, settings);

            // 1. Limit = 2
            settings.Current.LimitConcurrentTransfers = true;
            settings.Current.MaxConcurrentTransfers = 2;
            syncState.Reset();
            await hub.UploadDroppedFilesAsync(localFiles);
            Assert.Equal(8, syncState.TotalUploads);
            Assert.InRange(syncState.PeakUploads, 1, 2);

            // 2. Limit = 6
            settings.Current.LimitConcurrentTransfers = true;
            settings.Current.MaxConcurrentTransfers = 6;
            syncState.Reset();
            await hub.UploadDroppedFilesAsync(localFiles);
            Assert.Equal(8, syncState.TotalUploads);
            Assert.InRange(syncState.PeakUploads, 3, 6);

            // 3. Limit disabled -> all 8 run in parallel
            settings.Current.LimitConcurrentTransfers = false;
            syncState.Reset();
            await hub.UploadDroppedFilesAsync(localFiles);
            Assert.Equal(8, syncState.TotalUploads);
            Assert.True(syncState.PeakUploads >= 7, $"Expected >= 7 concurrent uploads without limit, got {syncState.PeakUploads}.");

            // Verify batch download obeys the same setting
            var downloadItems = Enumerable.Range(0, 8)
                .Select(i => new FileHubItemViewModel { Name = $"remote-{i}.txt", RemoteFileId = $"r-{i}", SizeBytes = 64 })
                .ToList();
            hub.UpdateSelectedItems(downloadItems);

            settings.Current.LimitConcurrentTransfers = true;
            settings.Current.MaxConcurrentTransfers = 2;
            syncState.Reset();
            await hub.DownloadSelectedCommand.ExecuteAsync(null);
            Assert.Equal(8, syncState.TotalDownloads);
            Assert.InRange(syncState.PeakDownloads, 1, 2);

            settings.Current.LimitConcurrentTransfers = false;
            syncState.Reset();
            await hub.DownloadSelectedCommand.ExecuteAsync(null);
            Assert.Equal(8, syncState.TotalDownloads);
            Assert.True(syncState.PeakDownloads >= 7, $"Expected >= 7 concurrent downloads without limit, got {syncState.PeakDownloads}.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DriveSyncService_SyncsMultipleFilesConcurrently_WithoutSqliteLockErrors()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-multisync-{Guid.NewGuid():N}");
        var localFolder = Path.Combine(tempRoot, "SyncRoot");
        var dbPath = Path.Combine(tempRoot, "custodia.db");
        Directory.CreateDirectory(localFolder);

        try
        {
            for (var i = 0; i < 8; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(localFolder, $"up-{i}.txt"), $"local-payload-{i}");
            }

            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var mockClient = DispatchProxy.Create<IGoogleDriveClient, InMemoryDriveClientProxy>();
            var driveState = (InMemoryDriveClientProxy)(object)mockClient;
            driveState.TransferDelayMs = 45;
            driveState.SeedRemoteFolder("remote-root", "SyncRoot", "root");
            for (var i = 0; i < 8; i++)
            {
                driveState.SeedRemoteFile($"dl-{i}.txt", "remote-root", $"remote-payload-{i}");
            }

            var settings = new InMemorySettingsService();
            settings.Current.LimitConcurrentTransfers = false;
            var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
            using var watcher = new LocalFolderWatcherService(settings, NullLogger<LocalFolderWatcherService>.Instance);
            using var syncService = new DriveSyncService(
                mockClient,
                repo,
                settings,
                auth,
                watcher,
                NullLogger<DriveSyncService>.Instance,
                connectivityProbe: _ => Task.FromResult(true));

            var folder = new LocalSyncFolder
            {
                Id = "sync-multi-1",
                AccountId = "default",
                LocalPath = localFolder,
                RemoteFolderId = "remote-root",
                RemoteFolderName = "SyncRoot",
                Mode = SyncMode.TwoWay
            };
            await repo.UpsertSyncFolderAsync(folder);

            await syncService.TriggerFolderSyncAsync(folder.Id);

            var syncedFolder = await repo.GetSyncFolderByIdAsync(folder.Id);
            Assert.NotNull(syncedFolder);
            Assert.Equal(FolderSyncState.Idle, syncedFolder.State);
            Assert.Null(syncedFolder.LastErrorMessage);
            Assert.Equal(16, syncedFolder.TrackedFileCount);
            Assert.True(driveState.PeakConcurrentTransfers >= 7, $"Expected >= 7 concurrent sync transfers when limit is off, got {driveState.PeakConcurrentTransfers}.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DriveSyncService_DeletingLocalFolder_TrashesEntireRemoteFolderOnce()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-del-local-dir-{Guid.NewGuid():N}");
        var localFolder = Path.Combine(tempRoot, "SyncRoot");
        var subFolder = Path.Combine(localFolder, "Projetos");
        var nestedFolder = Path.Combine(subFolder, "Nested");
        var keepEmptyDir = Path.Combine(localFolder, "Manter");
        var dbPath = Path.Combine(tempRoot, "custodia.db");
        Directory.CreateDirectory(nestedFolder);
        Directory.CreateDirectory(keepEmptyDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(subFolder, "a.txt"), "file-a");
            await File.WriteAllTextAsync(Path.Combine(subFolder, "b.txt"), "file-b");
            await File.WriteAllTextAsync(Path.Combine(nestedFolder, "c.txt"), "file-c");

            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var mockClient = DispatchProxy.Create<IGoogleDriveClient, InMemoryDriveClientProxy>();
            var driveState = (InMemoryDriveClientProxy)(object)mockClient;
            driveState.SeedRemoteFolder("remote-root", "SyncRoot", "root");

            var settings = new InMemorySettingsService();
            var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
            using var watcher = new LocalFolderWatcherService(settings, NullLogger<LocalFolderWatcherService>.Instance);
            using var syncService = new DriveSyncService(
                mockClient,
                repo,
                settings,
                auth,
                watcher,
                NullLogger<DriveSyncService>.Instance,
                connectivityProbe: _ => Task.FromResult(true));

            var folder = new LocalSyncFolder
            {
                Id = "sync-del-local",
                AccountId = "default",
                LocalPath = localFolder,
                RemoteFolderId = "remote-root",
                RemoteFolderName = "SyncRoot",
                Mode = SyncMode.TwoWay
            };
            await repo.UpsertSyncFolderAsync(folder);

            await syncService.TriggerFolderSyncAsync(folder.Id);

            var projetosId = driveState.GetFolderIdByName("Projetos", "remote-root");
            Assert.False(string.IsNullOrWhiteSpace(projetosId));
            driveState.TrashedIds.Clear();

            // Delete the entire local subfolder tree
            Directory.Delete(subFolder, recursive: true);

            await syncService.TriggerFolderSyncAsync(folder.Id);

            Assert.Single(driveState.TrashedIds);
            Assert.Equal(projetosId, driveState.TrashedIds[0]);

            var remainingTracked = await repo.GetTrackedFilesByFolderAsync(folder.Id);
            Assert.DoesNotContain(remainingTracked, t => t.RelativePath.StartsWith("Projetos", StringComparison.Ordinal));
            Assert.Contains(remainingTracked, t => t.IsFolder && t.RelativePath == "Manter");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DriveSyncService_DeletingRemoteFolder_MovesEntireLocalFolderToTrashOnce()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-del-remote-dir-{Guid.NewGuid():N}");
        var localFolder = Path.Combine(tempRoot, "SyncRoot");
        var trashDir = Path.Combine(tempRoot, "Trash");
        var subFolder = Path.Combine(localFolder, "Campanhas");
        var nestedFolder = Path.Combine(subFolder, "2026");
        var dbPath = Path.Combine(tempRoot, "custodia.db");
        Directory.CreateDirectory(nestedFolder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(subFolder, "plano.txt"), "plano");
            await File.WriteAllTextAsync(Path.Combine(nestedFolder, "artes.txt"), "artes");

            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var mockClient = DispatchProxy.Create<IGoogleDriveClient, InMemoryDriveClientProxy>();
            var driveState = (InMemoryDriveClientProxy)(object)mockClient;
            driveState.SeedRemoteFolder("remote-root", "SyncRoot", "root");

            var settings = new InMemorySettingsService();
            var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
            using var watcher = new LocalFolderWatcherService(settings, NullLogger<LocalFolderWatcherService>.Instance);
            using var syncService = new DriveSyncService(
                mockClient,
                repo,
                settings,
                auth,
                watcher,
                NullLogger<DriveSyncService>.Instance,
                connectivityProbe: _ => Task.FromResult(true))
            {
                TrashDirectoryOverride = trashDir
            };

            var deleteTransfers = new List<TransferQueueItem>();
            syncService.TransferUpdated += (_, item) =>
            {
                if (item.Direction == TransferDirection.Delete && item.Status == TransferStatus.Completed)
                {
                    deleteTransfers.Add(item);
                }
            };

            var folder = new LocalSyncFolder
            {
                Id = "sync-del-remote",
                AccountId = "default",
                LocalPath = localFolder,
                RemoteFolderId = "remote-root",
                RemoteFolderName = "SyncRoot",
                Mode = SyncMode.TwoWay
            };
            await repo.UpsertSyncFolderAsync(folder);

            await syncService.TriggerFolderSyncAsync(folder.Id);
            var campanhasId = driveState.GetFolderIdByName("Campanhas", "remote-root")!;
            driveState.CreatedFolderNames.Clear();
            deleteTransfers.Clear();

            // Trash the whole folder on Google Drive
            await mockClient.TrashFileAsync(campanhasId);

            await syncService.TriggerFolderSyncAsync(folder.Id);

            Assert.False(Directory.Exists(subFolder));
            Assert.DoesNotContain("Campanhas", driveState.CreatedFolderNames);
            Assert.Single(deleteTransfers);
            Assert.Equal("Campanhas", deleteTransfers[0].RelativePath);

            var trashedDirs = Directory.GetDirectories(Path.Combine(trashDir, "files"));
            Assert.Single(trashedDirs);
            Assert.True(File.Exists(Path.Combine(trashedDirs[0], "plano.txt")));
            Assert.True(File.Exists(Path.Combine(trashedDirs[0], "2026", "artes.txt")));

            Assert.Empty(await repo.GetTrackedFilesByFolderAsync(folder.Id));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void FileHubView_CheckBoxes_PreserveExistingMultiSelection()
    {
        var loc = new LocalizationService();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
        var repoProxy = DispatchProxy.Create<ISyncStateRepository, StubStateRepoProxy>();
        var hub = new FileHubViewModel(repoProxy, driveProxy, syncProxy, new FilePreviewService(driveProxy), loc);

        const string docSvg = "M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6z";
        var f1 = new FileHubItemViewModel { Name = "um.txt", RemoteFileId = "r1", SizeDisplay = "1 KB", FileIconData = docSvg };
        var f2 = new FileHubItemViewModel { Name = "dois.txt", RemoteFileId = "r2", SizeDisplay = "2 KB", FileIconData = docSvg };
        hub.Files.Add(f1);
        hub.Files.Add(f2);

        var view = new FileHubView { DataContext = hub };
        var window = new Window { Width = 900, Height = 640, Content = view };
        window.Show();
        window.UpdateLayout();

        var gridFilesList = view.FindControl<ListBox>("GridFilesListBox")!;
        var checkboxes = gridFilesList.GetVisualDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(2, checkboxes.Count);

        var clickMethod = typeof(FileHubView).GetMethod("OnItemCheckBoxClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        clickMethod.Invoke(view, [checkboxes[0], new Avalonia.Interactivity.RoutedEventArgs()]);
        clickMethod.Invoke(view, [checkboxes[1], new Avalonia.Interactivity.RoutedEventArgs()]);

        Assert.Equal(2, hub.SelectedCount);
        Assert.True(f1.IsSelected);
        Assert.True(f2.IsSelected);

        // Clicking checkbox[0] again removes only f1 and keeps f2 selected
        clickMethod.Invoke(view, [checkboxes[0], new Avalonia.Interactivity.RoutedEventArgs()]);
        Assert.Equal(1, hub.SelectedCount);
        Assert.False(f1.IsSelected);
        Assert.True(f2.IsSelected);

        window.Close();
    }

    [AvaloniaFact]
    public async Task FilePreviewService_PreviewsImageAndTextViaPreviewSelectedCommand()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-preview-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var pngPath = Path.Combine(tempRoot, "sample.png");
        // Valid 1x1 PNG bytes
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        await File.WriteAllBytesAsync(pngPath, pngBytes);

        try
        {
            var loc = new LocalizationService();
            loc.SetLanguage("pt-BR");
            var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
            var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
            var repoProxy = DispatchProxy.Create<ISyncStateRepository, StubStateRepoProxy>();
            using var previewService = new FilePreviewService(driveProxy);
            var hub = new FileHubViewModel(repoProxy, driveProxy, syncProxy, previewService, loc);

            var imgItem = new FileHubItemViewModel
            {
                Name = "sample.png",
                IsFolder = false,
                HasLocalFile = true,
                LocalFullPath = pngPath,
                MimeType = "image/png"
            };

            hub.UpdateSelectedItems([imgItem]);
            Assert.True(hub.CanPreviewSelected);

            hub.ClosePreview();
            Assert.False(hub.IsPreviewVisible);

            await hub.PreviewSelectedCommand.ExecuteAsync(null);
            Assert.True(hub.IsPreviewVisible);
            Assert.Equal(PreviewKind.Image, hub.CurrentPreviewKind);
            Assert.True(hub.HasVisualBitmap);
            Assert.NotNull(hub.PreviewBitmap);
            Assert.Equal(string.Empty, hub.PreviewMessage);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(0, 1 * 1024 * 1024)]
    [InlineData(512 * 1024, 1 * 1024 * 1024)]
    [InlineData(4L * 1024 * 1024, 4 * 1024 * 1024)]
    [InlineData(64L * 1024 * 1024, 8 * 1024 * 1024)]
    public void GoogleDriveClient_UsesValidMultiplesForTransferChunkSizes(long fileSizeBytes, int expectedChunkSize)
    {
        var chunk = GoogleDriveClient.ResolveUploadChunkSize(fileSizeBytes);
        Assert.Equal(expectedChunkSize, chunk);
        Assert.Equal(0, chunk % GoogleDriveClient.MinTransferChunkSizeBytes);
        Assert.Equal(0, GoogleDriveClient.DownloadChunkSizeBytes % GoogleDriveClient.MinTransferChunkSizeBytes);
        Assert.True(GoogleDriveClient.DownloadChunkSizeBytes <= 10 * 1024 * 1024);
    }

    private sealed class InMemorySettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public event EventHandler<AppSettings>? SettingsChanged;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }

        public Task SaveSilentlyAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private class BasicSettingsStubProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "get_IsRunning" => false,
                "get_IsSupported" => false,
                "IsEnabled" => false,
                "get_BoundAddress" => "http://127.0.0.1:5080",
                "get_DesktopEntryPath" => "/tmp/custodia.desktop",
                _ => null
            };
    }

    private class ConcurrencyTrackingSyncProxy : StubDriveSyncProxy
    {
        private int _activeUploads;
        private int _peakUploads;
        private int _totalUploads;
        private int _activeDownloads;
        private int _peakDownloads;
        private int _totalDownloads;

        public int PeakUploads => Volatile.Read(ref _peakUploads);
        public int TotalUploads => Volatile.Read(ref _totalUploads);
        public int PeakDownloads => Volatile.Read(ref _peakDownloads);
        public int TotalDownloads => Volatile.Read(ref _totalDownloads);

        public void Reset()
        {
            Interlocked.Exchange(ref _activeUploads, 0);
            Interlocked.Exchange(ref _peakUploads, 0);
            Interlocked.Exchange(ref _totalUploads, 0);
            Interlocked.Exchange(ref _activeDownloads, 0);
            Interlocked.Exchange(ref _peakDownloads, 0);
            Interlocked.Exchange(ref _totalDownloads, 0);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDriveSyncService.UploadSingleFileAsync))
            {
                return TrackUploadAsync((string)args![0]!);
            }

            if (targetMethod?.Name == nameof(IDriveSyncService.DownloadRemoteFileAsync))
            {
                return TrackDownloadAsync((string)args![1]!);
            }

            return base.Invoke(targetMethod, args);
        }

        private async Task<RemoteDriveNode> TrackUploadAsync(string path)
        {
            var cur = Interlocked.Increment(ref _activeUploads);
            UpdateMax(ref _peakUploads, cur);
            try
            {
                await Task.Delay(75);
                Interlocked.Increment(ref _totalUploads);
                return new RemoteDriveNode { Id = Guid.NewGuid().ToString("N"), Name = Path.GetFileName(path) };
            }
            finally
            {
                Interlocked.Decrement(ref _activeUploads);
            }
        }

        private async Task<string> TrackDownloadAsync(string fileName)
        {
            var cur = Interlocked.Increment(ref _activeDownloads);
            UpdateMax(ref _peakDownloads, cur);
            try
            {
                await Task.Delay(75);
                Interlocked.Increment(ref _totalDownloads);
                return $"/tmp/{fileName}";
            }
            finally
            {
                Interlocked.Decrement(ref _activeDownloads);
            }
        }

        private static void UpdateMax(ref int target, int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref target);
                if (value <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref target, value, current) == current)
                {
                    return;
                }
            }
        }
    }

    private class InMemoryDriveClientProxy : DispatchProxy
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, RemoteEntry> _entries = new(StringComparer.Ordinal);
        private int _activeTransfers;
        private int _peakConcurrentTransfers;

        public int TransferDelayMs { get; set; }
        public int PeakConcurrentTransfers => Volatile.Read(ref _peakConcurrentTransfers);
        public List<string> TrashedIds { get; } = [];
        public List<string> CreatedFolderNames { get; } = [];

        public void SeedRemoteFolder(string id, string name, string parentId)
        {
            lock (_gate)
            {
                _entries[id] = new RemoteEntry
                {
                    Id = id,
                    Name = name,
                    ParentId = parentId,
                    IsFolder = true,
                    ModifiedUtc = DateTimeOffset.UtcNow
                };
            }
        }

        public void SeedRemoteFile(string name, string parentId, string content)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
            var id = $"file-{Guid.NewGuid():N}";
            lock (_gate)
            {
                _entries[id] = new RemoteEntry
                {
                    Id = id,
                    Name = name,
                    ParentId = parentId,
                    IsFolder = false,
                    Content = bytes,
                    Md5 = md5,
                    ModifiedUtc = DateTimeOffset.UtcNow
                };
            }
        }

        public string? GetFolderIdByName(string name, string parentId)
        {
            lock (_gate)
            {
                return _entries.Values
                    .FirstOrDefault(e => e.IsFolder && !e.Trashed && e.Name == name && e.ParentId == parentId)
                    ?.Id;
            }
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_IsConnected":
                    return true;
                case "get_ActiveAccountKey":
                    return "default";
                case nameof(IGoogleDriveClient.GetAccountInfoAsync):
                    return Task.FromResult(new DriveAccount { Id = "default", IsConnected = true, EmailAddress = "test@example.com" });
                case nameof(IGoogleDriveClient.ListChildrenAsync):
                {
                    var parentId = (string)args![0]!;
                    var foldersOnly = (bool)args[1]!;
                    lock (_gate)
                    {
                        var list = _entries.Values
                            .Where(e => !e.Trashed && e.ParentId == parentId && (!foldersOnly || e.IsFolder))
                            .Select(ToNode)
                            .ToList();
                        return Task.FromResult<IReadOnlyList<RemoteDriveNode>>(list);
                    }
                }
                case nameof(IGoogleDriveClient.CreateFolderAsync):
                case nameof(IGoogleDriveClient.FindOrCreateFolderAsync):
                {
                    var name = (string)args![0]!;
                    var parentId = (string)args[1]!;
                    lock (_gate)
                    {
                        var existing = _entries.Values.FirstOrDefault(e =>
                            e.IsFolder && !e.Trashed && e.ParentId == parentId && e.Name == name);
                        if (existing is not null && targetMethod.Name == nameof(IGoogleDriveClient.FindOrCreateFolderAsync))
                        {
                            return Task.FromResult(ToNode(existing));
                        }

                        var entry = new RemoteEntry
                        {
                            Id = $"dir-{Guid.NewGuid():N}",
                            Name = name,
                            ParentId = parentId,
                            IsFolder = true,
                            ModifiedUtc = DateTimeOffset.UtcNow
                        };
                        _entries[entry.Id] = entry;
                        CreatedFolderNames.Add(name);
                        return Task.FromResult(ToNode(entry));
                    }
                }
                case nameof(IGoogleDriveClient.UploadFileAsync):
                    return UploadAsync((string)args![0]!, (string)args[1]!, (string?)args[2], (Action<long>?)args[3]);
                case nameof(IGoogleDriveClient.DownloadFileAsync):
                    return DownloadAsync((string)args![0]!, (string)args[1]!, (Action<long>?)args[2]);
                case nameof(IGoogleDriveClient.TrashFileAsync):
                {
                    var id = (string)args![0]!;
                    lock (_gate)
                    {
                        if (_entries.TryGetValue(id, out var entry))
                        {
                            entry.Trashed = true;
                        }
                        TrashedIds.Add(id);
                    }
                    return Task.CompletedTask;
                }
                default:
                    return null;
            }
        }

        private async Task<RemoteDriveNode> UploadAsync(string localPath, string parentId, string? existingId, Action<long>? onBytes)
        {
            var cur = Interlocked.Increment(ref _activeTransfers);
            UpdatePeak(cur);
            try
            {
                if (TransferDelayMs > 0)
                {
                    await Task.Delay(TransferDelayMs);
                }

                var bytes = await File.ReadAllBytesAsync(localPath);
                var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
                onBytes?.Invoke(bytes.Length);

                lock (_gate)
                {
                    var id = string.IsNullOrWhiteSpace(existingId) ? $"file-{Guid.NewGuid():N}" : existingId;
                    var entry = new RemoteEntry
                    {
                        Id = id,
                        Name = Path.GetFileName(localPath),
                        ParentId = parentId,
                        IsFolder = false,
                        Content = bytes,
                        Md5 = md5,
                        ModifiedUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(localPath), TimeSpan.Zero)
                    };
                    _entries[id] = entry;
                    return ToNode(entry);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeTransfers);
            }
        }

        private async Task DownloadAsync(string remoteId, string destPath, Action<long>? onBytes)
        {
            var cur = Interlocked.Increment(ref _activeTransfers);
            UpdatePeak(cur);
            try
            {
                if (TransferDelayMs > 0)
                {
                    await Task.Delay(TransferDelayMs);
                }

                byte[] bytes;
                lock (_gate)
                {
                    bytes = _entries[remoteId].Content;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                await File.WriteAllBytesAsync(destPath, bytes);
                onBytes?.Invoke(bytes.Length);
            }
            finally
            {
                Interlocked.Decrement(ref _activeTransfers);
            }
        }

        private void UpdatePeak(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _peakConcurrentTransfers);
                if (value <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _peakConcurrentTransfers, value, current) == current)
                {
                    return;
                }
            }
        }

        private static RemoteDriveNode ToNode(RemoteEntry e) => new()
        {
            Id = e.Id,
            Name = e.Name,
            MimeType = e.IsFolder ? MimeTypeHelper.GoogleFolderMimeType : "application/octet-stream",
            IsFolder = e.IsFolder,
            SizeBytes = e.Content.Length,
            Md5Checksum = e.Md5,
            ModifiedTimeUtc = e.ModifiedUtc,
            ParentIds = [e.ParentId],
            Trashed = e.Trashed
        };

        private sealed class RemoteEntry
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string ParentId { get; set; } = string.Empty;
            public bool IsFolder { get; set; }
            public byte[] Content { get; set; } = [];
            public string? Md5 { get; set; }
            public DateTimeOffset ModifiedUtc { get; set; }
            public bool Trashed { get; set; }
        }
    }
}
