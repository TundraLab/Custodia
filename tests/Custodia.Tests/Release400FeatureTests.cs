using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using TundraDrive.Api;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;
using TundraDrive.Core.Updates;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Tests;

public sealed class Release400FeatureTests
{
    [Fact]
    public async Task SqliteRepository_PersistsAndDeletesResumeSessions()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-resume-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var dbPath = Path.Combine(tempRoot, "state.db");
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var session = new TransferResumeSession
            {
                TransferKey = "upload:folder1:/tmp/big.bin",
                Direction = TransferDirection.Upload,
                LocalPath = "/tmp/big.bin",
                RemoteParentId = "remote-parent",
                UploadSessionUri = "https://www.googleapis.com/upload/drive/v3/files?upload_id=abc123",
                TotalBytes = 10_485_760,
                TransferredBytes = 4_194_304,
                LocalMtimeTicks = 123456789L,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            await repo.UpsertResumeSessionAsync(session);
            var loaded = await repo.GetResumeSessionAsync(session.TransferKey);

            Assert.NotNull(loaded);
            Assert.Equal(session.UploadSessionUri, loaded.UploadSessionUri);
            Assert.Equal(4_194_304, loaded.TransferredBytes);
            Assert.Equal(123456789L, loaded.LocalMtimeTicks);

            await repo.DeleteResumeSessionAsync(session.TransferKey);
            var afterDelete = await repo.GetResumeSessionAsync(session.TransferKey);
            Assert.Null(afterDelete);
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
    public async Task BandwidthLimiter_RespectsRateLimitAndSkipsWhenDisabled()
    {
        var limiter = new TransferBandwidthLimiter();

        // Disabled limit returns immediately.
        var disabledSw = Stopwatch.StartNew();
        await limiter.ThrottleAsync(256 * 1024, 0, CancellationToken.None);
        disabledSw.Stop();
        Assert.True(disabledSw.ElapsedMilliseconds < 50);

        // With 100 KB/s limit, bursting 60 KB after 50 KB takes measurable pacing delay.
        await limiter.ThrottleAsync(50 * 1024, 100 * 1024, CancellationToken.None);
        var limitedSw = Stopwatch.StartNew();
        await limiter.ThrottleAsync(60 * 1024, 100 * 1024, CancellationToken.None);
        limitedSw.Stop();
        Assert.True(limitedSw.ElapsedMilliseconds >= 45, $"Expected throttled delay >=45ms, got {limitedSw.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task TransferQueueViewModel_FiltersAndRetriesFailedItems()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("en-US");
        var syncProxy = DispatchProxy.Create<IDriveSyncService, QueueRetrySyncProxy>();
        var stubSync = (QueueRetrySyncProxy)(object)syncProxy;

        stubSync.Seed([
            new TransferQueueItem { Id = "u1", FileName = "up.zip", Direction = TransferDirection.Upload, Status = TransferStatus.Completed, TotalBytes = 100, TransferredBytes = 100 },
            new TransferQueueItem { Id = "d1", FileName = "down.iso", Direction = TransferDirection.Download, Status = TransferStatus.Failed, ErrorMessage = "Timeout", TotalBytes = 200, TransferredBytes = 50 },
            new TransferQueueItem { Id = "u2", FileName = "up-fail.mp4", Direction = TransferDirection.Upload, Status = TransferStatus.Failed, ErrorMessage = "Offline", TotalBytes = 300, TransferredBytes = 0 }
        ]);

        var vm = new TransferQueueViewModel(syncProxy, loc);
        Assert.Equal(3, vm.FilteredItems.Count);

        vm.SelectUploadsFilterCommand.Execute(null);
        Assert.Equal(2, vm.FilteredItems.Count);
        Assert.All(vm.FilteredItems, item => Assert.Equal(TransferDirection.Upload, item.Direction));

        vm.SelectDownloadsFilterCommand.Execute(null);
        Assert.Single(vm.FilteredItems);
        Assert.Equal("d1", vm.FilteredItems[0].Id);
        Assert.True(vm.FilteredItems[0].CanRetry);

        await vm.FilteredItems[0].RetryCommand.ExecuteAsync(null);
        Assert.Contains("d1", stubSync.RetriedIds);

        vm.SelectFailedFilterCommand.Execute(null);
        Assert.Equal(2, vm.FilteredItems.Count);

        await vm.RetryAllFailedCommand.ExecuteAsync(null);
        Assert.True(stubSync.RetryAllCalled);
    }

    [Fact]
    public async Task DriveSyncService_DownloadToCustomPathAndRetry_PreservesTempResumeAndCompletes()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-dlto-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var customDest = Path.Combine(tempRoot, "chosen", "custom-photo.jpg");
            var dbPath = Path.Combine(tempRoot, "state.db");
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var mockClient = new FakeResumableDriveClient();
            var settings = new FakeSettingsService(tempRoot);
            var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
            using var watcher = new LocalFolderWatcherService(settings, NullLogger<LocalFolderWatcherService>.Instance);
            using var sync = new DriveSyncService(
                mockClient,
                repo,
                settings,
                auth,
                watcher,
                NullLogger<DriveSyncService>.Instance,
                connectivityProbe: _ => Task.FromResult(true));

            // First attempt simulates connection drop halfway while retaining .tundratmp file.
            mockClient.FailDownloadOnFirstAttempt = true;
            await Assert.ThrowsAsync<IOException>(() => sync.DownloadRemoteFileToAsync(
                "file-99",
                "photo.jpg",
                customDest,
                treatAsFullFilePath: true,
                sizeBytes: 1024));

            var snapshotAfterFail = sync.GetTransferQueueSnapshot();
            var failedTransfer = Assert.Single(snapshotAfterFail);
            Assert.Equal(TransferStatus.Failed, failedTransfer.Status);
            Assert.Equal(customDest, failedTransfer.LocalPath);
            Assert.True(failedTransfer.CanRetry);
            Assert.False(string.IsNullOrWhiteSpace(mockClient.LastTempPath));
            Assert.True(File.Exists(mockClient.LastTempPath));

            // Retry resumes with the retained temp path and finalizes customDest.
            var retried = await sync.RetryTransferAsync(failedTransfer.Id);
            Assert.True(retried);
            Assert.True(File.Exists(customDest));
            Assert.False(string.IsNullOrWhiteSpace(mockClient.ResumedWithTempPath));
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
    public async Task FileHubViewModel_SharedWithMe_SharePermissions_SaveAs_AndThumbnails_WorkCleanly()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");
        var settings = new FakeSettingsService(Path.GetTempPath());
        var client = new FeatureRichDriveClient();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, FeatureRichSyncProxy>();
        var stubSync = (FeatureRichSyncProxy)(object)syncProxy;
        stubSync.Client = client;
        var repoProxy = DispatchProxy.Create<ISyncStateRepository, EmptyRepoProxy>();

        using var preview = new FilePreviewService(client);
        using var hub = new FileHubViewModel(repoProxy, client, syncProxy, preview, loc, settings);

        // 1. ThumbnailLink fast caching avoids downloading full files.
        var thumbRemoteId = $"photo-fast-{Guid.NewGuid():N}";
        var tempThumbTarget = new RemoteDriveNode
        {
            Id = thumbRemoteId,
            Name = "festa.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 8 * 1024 * 1024,
            ThumbnailLink = "https://lh3.googleusercontent.com/mock-thumb",
            ModifiedTimeUtc = DateTimeOffset.UtcNow
        };
        var png1x1 = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
        client.ThumbnailPayloads[thumbRemoteId] = png1x1;
        var thumbBitmap = await preview.TryLoadGridThumbnailAsync(
            tempThumbTarget.Name,
            null,
            tempThumbTarget.Id,
            tempThumbTarget.SizeBytes,
            CancellationToken.None,
            tempThumbTarget.ThumbnailLink);
        Assert.NotNull(thumbBitmap);
        thumbBitmap.Dispose();
        Assert.Equal(0, client.FullDownloadCount);
        Assert.Equal(1, client.ThumbnailFetchCount);

        // 2. Shared With Me tab + folder navigation + Save As.
        hub.SelectSharedWithMeFilterCommand.Execute(null);
        await hub.RefreshAsync();
        Assert.True(hub.IsSharedWithMeView);
        Assert.Equal(2, hub.Files.Count);

        var sharedFolder = hub.Files.Single(f => f.RemoteFileId == "shared-folder-1");
        await hub.ActivateItemAsync(sharedFolder);
        Assert.True(hub.IsSharedWithMeView);
        Assert.Single(hub.Files);
        Assert.Equal("shared-doc-in-folder.pdf", hub.Files[0].Name);

        await hub.NavigateBackAsync();
        Assert.Equal(2, hub.Files.Count);

        var sharedFile = hub.Files.Single(f => f.RemoteFileId == "shared-photo-1");
        hub.UpdateSelectedItems([sharedFile]);
        Assert.True(hub.CanShareSelected);

        // Single-item preview open hides floating selection bar to prevent clutter.
        await hub.PreviewSelectedCommand.ExecuteAsync(null);
        Assert.True(hub.IsPreviewVisible);
        Assert.True(hub.ShowSelectionBar);
        Assert.False(hub.ShowFloatingSelectionBar);

        hub.ClosePreview();
        Assert.True(hub.ShowFloatingSelectionBar);

        // Save As downloads to explicit path.
        hub.PickSaveFileCallback = _ => Task.FromResult<string?>("/tmp/custom-export/foto-salva.jpg");
        await hub.DownloadSelectedAsCommand.ExecuteAsync(null);
        Assert.Single(stubSync.DownloadToCalls);
        Assert.Equal("/tmp/custom-export/foto-salva.jpg", stubSync.DownloadToCalls[0].Destination);
        Assert.True(stubSync.DownloadToCalls[0].FullPath);

        // 3. Share & Permissions panel lifecycle.
        await hub.OpenShareCommand.ExecuteAsync(null);
        Assert.True(hub.IsShareOpen);

        await hub.EnableShareLinkReadCommand.ExecuteAsync(null);
        Assert.True(hub.HasShareLink);
        Assert.Equal("reader", hub.ShareAnyoneRole);

        hub.ShareInviteEmail = "colega@tundralab.com";
        hub.SelectedShareInviteRoleIndex = 1;
        await hub.InviteShareEmailCommand.ExecuteAsync(null);
        Assert.Contains(client.Permissions["shared-photo-1"], p => p.EmailAddress == "colega@tundralab.com" && p.Role == "writer");

        await hub.RevokeShareLinkCommand.ExecuteAsync(null);
        Assert.False(hub.HasShareLink);
    }

    [Fact]
    public async Task SettingsViewModel_PersistsBandwidthLimitSetting()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-bw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var settingsService = new FakeSettingsService(tempRoot);
            var loc = new LocalizationService();
            var apiProxy = DispatchProxy.Create<IEmbeddedApiServer, ApiAutostartStubProxy>();
            var autostartProxy = DispatchProxy.Create<IAutostartManager, ApiAutostartStubProxy>();
            var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
            var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
            var notify = new RecordingNotificationService();
            using var cleaner = new MemoryCleanerService(settingsService, NullLogger<MemoryCleanerService>.Instance);
            var vm = new SettingsViewModel(settingsService, apiProxy, autostartProxy, loc, notify, auth, syncProxy, cleaner);

            vm.LimitTransferBandwidth = true;
            vm.MaxTransferBandwidthKbps = 4096;
            await vm.SaveSettingsCommand.ExecuteAsync(null);

            Assert.True(settingsService.Current.LimitTransferBandwidth);
            Assert.Equal(4096, settingsService.Current.MaxTransferBandwidthKbps);
            Assert.Equal(4096 * 1024L, settingsService.Current.GetEffectiveBandwidthLimitBytesPerSecond());
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
    public async Task InstallerViewModel_TracksStepsMetricsAndCompletion()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-inst-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var tempPkg = Path.Combine(tempRoot, "Custodia-4.0.0.AppImage");
        await File.WriteAllBytesAsync(tempPkg, new byte[2048]);
        var homeInstall = Path.Combine(tempRoot, "installed", "Custodia.AppImage");
        try
        {
            var loc = new LocalizationService();
            loc.SetLanguage("pt-BR");
            var updater = new FakeAppUpdateService(tempPkg);
            using var vm = new InstallerViewModel(updater, loc)
            {
                AppImageTargetPathOverride = homeInstall
            };

            Assert.Equal(5, vm.Steps.Count);
            Assert.False(vm.IsCompleted);

            vm.ToggleDetailsCommand.Execute(null);
            Assert.True(vm.IsDetailsOpen);

            await vm.RunInstallationAsync();

            Assert.True(vm.IsCompleted);
            Assert.False(vm.IsFailed);
            Assert.Equal(100, vm.ProgressPercentage);
            Assert.All(vm.Steps, s => Assert.Equal(InstallerStepState.Completed, s.State));
            Assert.True(File.Exists(homeInstall));
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
    public async Task ActivityLogsViewModel_RefreshSetsIsRefreshingDuringReload()
    {
        var loc = new LocalizationService();
        var repoProxy = DispatchProxy.Create<ISyncStateRepository, EmptyRepoProxy>();
        var vm = new ActivityLogsViewModel(repoProxy, loc);

        var observedStates = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActivityLogsViewModel.IsRefreshing))
            {
                observedStates.Add(vm.IsRefreshing);
            }
        };

        await vm.RefreshLogsAsync();
        Assert.Equal([true, false], observedStates);
        Assert.False(vm.IsRefreshing);
    }

    private sealed class FakeAppUpdateService : IAppUpdateService
    {
        private readonly string _pkgPath;
        public FakeAppUpdateService(string pkgPath) => _pkgPath = pkgPath;
        public AppInstallKind DetectInstallKind(out string? installedPackagePath)
        {
            installedPackagePath = null;
            return AppInstallKind.AppImage;
        }

        public Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppUpdateCheckResult
            {
                CurrentVersion = "3.0.5",
                LatestVersion = "4.0.0",
                TagName = "v4.0.0",
                IsUpdateAvailable = true,
                ReleaseUrl = "https://github.com/TundraLab/Custodia/releases/tag/v4.0.0",
                AppImageAsset = new ReleaseAssetInfo("Custodia-4.0.0-x86_64.AppImage", "https://example.com/app", 2048),
                InstallKind = AppInstallKind.AppImage
            });

        public Task<DownloadedUpdatePackage> DownloadUpdatePackageAsync(
            AppUpdateCheckResult checkResult,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var asset = checkResult.PreferredAsset!;
            progress?.Report(new UpdateDownloadProgress(1024, 2048, 50.0, 4.0));
            return Task.FromResult(new DownloadedUpdatePackage(checkResult, asset, _pkgPath, true));
        }

        public Task<UpdateApplyLaunchResult> LaunchUpdateInstallerAsync(DownloadedUpdatePackage package, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateApplyLaunchResult(true, false, _pkgPath));
    }

    private class ApiAutostartStubProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.ReturnType == typeof(Task))
                return Task.CompletedTask;
            if (targetMethod?.ReturnType == typeof(Task<bool>))
                return Task.FromResult(false);
            if (targetMethod?.ReturnType == typeof(bool))
                return false;
            return null;
        }
    }

    private sealed class FakeSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; }
        public event EventHandler<AppSettings>? SettingsChanged;
        public FakeSettingsService(string downloadDir) => Current = new AppSettings { DownloadDirectory = downloadDir };
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

    private class QueueRetrySyncProxy : DispatchProxy
    {
        private List<TransferQueueItem> _items = [];
        public List<string> RetriedIds { get; } = [];
        public bool RetryAllCalled { get; private set; }
        public void Seed(IEnumerable<TransferQueueItem> items) => _items = items.ToList();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                $"get_{nameof(IDriveSyncService.CurrentAccount)}" => new DriveAccount { IsConnected = true, EmailAddress = "u@test.com" },
                $"get_{nameof(IDriveSyncService.SavedAccounts)}" => Array.Empty<DriveAccount>(),
                $"get_{nameof(IDriveSyncService.IsOnline)}" => true,
                nameof(IDriveSyncService.GetTransferQueueSnapshot) => _items.ToList(),
                nameof(IDriveSyncService.GetTransferQueueStats) => new TransferQueueStats(0, _items.Count(i => i.Status == TransferStatus.Completed), _items.Count(i => i.Status == TransferStatus.Failed), 0, _items.Count),
                nameof(IDriveSyncService.RetryTransferAsync) => RecordRetry((string)args![0]!),
                nameof(IDriveSyncService.RetryAllFailedTransfersAsync) => RecordRetryAll(),
                _ => null
            };

        private Task<bool> RecordRetry(string id)
        {
            RetriedIds.Add(id);
            return Task.FromResult(true);
        }

        private Task<int> RecordRetryAll()
        {
            RetryAllCalled = true;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeResumableDriveClient : IGoogleDriveClient, IResumableDriveClient
    {
        public bool IsConnected => true;
        public string ActiveAccountKey => "default";
        public bool FailDownloadOnFirstAttempt { get; set; }
        public string? LastTempPath { get; private set; }
        public string? ResumedWithTempPath { get; private set; }
        private int _attempt;

        public Task<byte[]?> DownloadThumbnailAsync(string remoteFileId, string? thumbnailLink, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public async Task ResumableDownloadAsync(
            string remoteFileId,
            string destinationLocalPath,
            string? resumeTempPath = null,
            long knownTotalBytes = 0,
            Func<string, Task>? onTempPathAssigned = null,
            Action<long>? onBytesTransferred = null,
            Func<long>? getBandwidthLimitBytesPerSecond = null,
            TransferBandwidthLimiter? bandwidthLimiter = null,
            bool keepTempOnInterrupt = true,
            CancellationToken cancellationToken = default)
        {
            var tempPath = string.IsNullOrWhiteSpace(resumeTempPath) ? $"{destinationLocalPath}.tundratmp.mock" : resumeTempPath;
            ResumedWithTempPath = resumeTempPath;
            LastTempPath = tempPath;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationLocalPath)!);
            if (onTempPathAssigned is not null)
            {
                await onTempPathAssigned(tempPath);
            }

            if (Interlocked.Increment(ref _attempt) == 1 && FailDownloadOnFirstAttempt)
            {
                await File.WriteAllBytesAsync(tempPath, new byte[512], cancellationToken);
                onBytesTransferred?.Invoke(512);
                throw new IOException("Simulated connection drop");
            }

            await File.WriteAllBytesAsync(tempPath, new byte[1024], cancellationToken);
            File.Move(tempPath, destinationLocalPath, overwrite: true);
            onBytesTransferred?.Invoke(1024);
        }

        public Task<RemoteDriveNode> ResumableUploadAsync(
            string localFilePath,
            string parentFolderId,
            string? existingRemoteFileId = null,
            string? resumeSessionUri = null,
            Func<string, Task>? onSessionCreated = null,
            Action<long>? onBytesTransferred = null,
            Func<long>? getBandwidthLimitBytesPerSecond = null,
            TransferBandwidthLimiter? bandwidthLimiter = null,
            CancellationToken cancellationToken = default) =>
            UploadFileAsync(localFilePath, parentFolderId, existingRemoteFileId, onBytesTransferred, cancellationToken);

        public Task<DriveAccount> ConnectInteractiveAsync(string userKey = "default", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DriveAccount?> TryRestoreSessionAsync(string userKey = "default", CancellationToken cancellationToken = default) => Task.FromResult<DriveAccount?>(new DriveAccount { IsConnected = true });
        public Task<DriveAccount?> SwitchSessionAsync(string userKey, CancellationToken cancellationToken = default) => Task.FromResult<DriveAccount?>(null);
        public Task DisconnectAsync(string? userKey = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DriveAccount> GetAccountInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DriveAccount { IsConnected = true });
        public Task<IReadOnlyList<RemoteDriveNode>> ListChildrenAsync(string parentFolderId = "root", bool foldersOnly = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListAllRemoteFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListStarredFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListTrashedFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> SearchRemoteFilesAsync(string query, int maxResults = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<RemoteDriveNode?> GetFileMetadataAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.FromResult<RemoteDriveNode?>(null);
        public Task<RemoteDriveNode> CreateFolderAsync(string folderName, string parentFolderId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RemoteDriveNode> FindOrCreateFolderAsync(string folderName, string parentFolderId = "root", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RemoteDriveNode> UploadFileAsync(string localFilePath, string parentFolderId, string? existingRemoteFileId = null, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteDriveNode { Id = "up-1", Name = Path.GetFileName(localFilePath) });
        public Task<RemoteDriveNode> MoveFileAsync(string remoteFileId, string? destinationParentFolderId = null, string? newName = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DownloadFileAsync(string remoteFileId, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExportFileAsync(string remoteFileId, string exportMimeType, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetStarredAsync(string remoteFileId, bool starred, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TrashFileAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreFileAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeletePermanentlyAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EmptyTrashAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FeatureRichDriveClient : IGoogleDriveClient, IResumableDriveClient
    {
        public bool IsConnected => true;
        public string ActiveAccountKey => "default";
        public int FullDownloadCount { get; private set; }
        public int ThumbnailFetchCount { get; private set; }
        public Dictionary<string, byte[]> ThumbnailPayloads { get; } = new();
        public Dictionary<string, List<DrivePermissionEntry>> Permissions { get; } = new()
        {
            ["shared-photo-1"] =
            [
                new DrivePermissionEntry { Id = "owner-1", Type = "user", Role = "owner", EmailAddress = "owner@tundralab.com" }
            ]
        };

        public Task<byte[]?> DownloadThumbnailAsync(string remoteFileId, string? thumbnailLink, CancellationToken cancellationToken = default)
        {
            ThumbnailFetchCount++;
            return Task.FromResult(ThumbnailPayloads.TryGetValue(remoteFileId, out var bytes) ? bytes : null);
        }

        public Task<IReadOnlyList<RemoteDriveNode>> ListSharedWithMeAsync(int maxResults = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteDriveNode>>(
            [
                new RemoteDriveNode { Id = "shared-folder-1", Name = "Pasta Compartilhada", MimeType = MimeTypeHelper.GoogleFolderMimeType, IsFolder = true, ModifiedTimeUtc = DateTimeOffset.UtcNow },
                new RemoteDriveNode { Id = "shared-photo-1", Name = "shared-photo.jpg", MimeType = "image/jpeg", SizeBytes = 2048, ModifiedTimeUtc = DateTimeOffset.UtcNow }
            ]);

        public Task<IReadOnlyList<RemoteDriveNode>> ListChildrenAsync(string parentFolderId = "root", bool foldersOnly = false, CancellationToken cancellationToken = default)
        {
            if (parentFolderId == "shared-folder-1")
            {
                return Task.FromResult<IReadOnlyList<RemoteDriveNode>>(
                [
                    new RemoteDriveNode { Id = "shared-child-1", Name = "shared-doc-in-folder.pdf", MimeType = "application/pdf", SizeBytes = 512, ModifiedTimeUtc = DateTimeOffset.UtcNow }
                ]);
            }

            return Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        }

        public Task<string?> GetShareLinkAsync(string remoteFileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>($"https://drive.google.com/file/d/{remoteFileId}/view");

        public Task<IReadOnlyList<DrivePermissionEntry>> ListPermissionsAsync(string remoteFileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DrivePermissionEntry>>(
                Permissions.TryGetValue(remoteFileId, out var list) ? list.ToList() : []);

        public Task<DrivePermissionEntry> ShareAnyoneAsync(string remoteFileId, string role, CancellationToken cancellationToken = default)
        {
            var list = Permissions.GetValueOrDefault(remoteFileId) ?? [];
            Permissions[remoteFileId] = list;
            list.RemoveAll(p => p.Type == "anyone");
            var entry = new DrivePermissionEntry { Id = "anyoneWithLink", Type = "anyone", Role = role };
            list.Add(entry);
            return Task.FromResult(entry);
        }

        public Task<DrivePermissionEntry> InviteUserPermissionAsync(string remoteFileId, string emailAddress, string role, bool sendNotificationEmail = true, CancellationToken cancellationToken = default)
        {
            var list = Permissions.GetValueOrDefault(remoteFileId) ?? [];
            Permissions[remoteFileId] = list;
            var entry = new DrivePermissionEntry { Id = $"user-{list.Count + 1}", Type = "user", Role = role, EmailAddress = emailAddress };
            list.Add(entry);
            return Task.FromResult(entry);
        }

        public Task RevokePermissionAsync(string remoteFileId, string permissionId, CancellationToken cancellationToken = default)
        {
            if (Permissions.TryGetValue(remoteFileId, out var list))
            {
                list.RemoveAll(p => p.Id == permissionId);
            }

            return Task.CompletedTask;
        }

        public Task DownloadFileAsync(string remoteFileId, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default)
        {
            FullDownloadCount++;
            return Task.CompletedTask;
        }

        public Task ResumableDownloadAsync(string remoteFileId, string destinationLocalPath, string? resumeTempPath = null, long knownTotalBytes = 0, Func<string, Task>? onTempPathAssigned = null, Action<long>? onBytesTransferred = null, Func<long>? getBandwidthLimitBytesPerSecond = null, TransferBandwidthLimiter? bandwidthLimiter = null, bool keepTempOnInterrupt = true, CancellationToken cancellationToken = default)
        {
            FullDownloadCount++;
            return Task.CompletedTask;
        }

        public Task<RemoteDriveNode> ResumableUploadAsync(string localFilePath, string parentFolderId, string? existingRemoteFileId = null, string? resumeSessionUri = null, Func<string, Task>? onSessionCreated = null, Action<long>? onBytesTransferred = null, Func<long>? getBandwidthLimitBytesPerSecond = null, TransferBandwidthLimiter? bandwidthLimiter = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DriveAccount> ConnectInteractiveAsync(string userKey = "default", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DriveAccount?> TryRestoreSessionAsync(string userKey = "default", CancellationToken cancellationToken = default) => Task.FromResult<DriveAccount?>(new DriveAccount { IsConnected = true });
        public Task<DriveAccount?> SwitchSessionAsync(string userKey, CancellationToken cancellationToken = default) => Task.FromResult<DriveAccount?>(null);
        public Task DisconnectAsync(string? userKey = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DriveAccount> GetAccountInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DriveAccount { IsConnected = true });
        public Task<IReadOnlyList<RemoteDriveNode>> ListAllRemoteFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListStarredFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListTrashedFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> SearchRemoteFilesAsync(string query, int maxResults = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<RemoteDriveNode?> GetFileMetadataAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.FromResult<RemoteDriveNode?>(null);
        public Task<RemoteDriveNode> CreateFolderAsync(string folderName, string parentFolderId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RemoteDriveNode> FindOrCreateFolderAsync(string folderName, string parentFolderId = "root", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RemoteDriveNode> UploadFileAsync(string localFilePath, string parentFolderId, string? existingRemoteFileId = null, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RemoteDriveNode> MoveFileAsync(string remoteFileId, string? destinationParentFolderId = null, string? newName = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExportFileAsync(string remoteFileId, string exportMimeType, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetStarredAsync(string remoteFileId, bool starred, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TrashFileAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreFileAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeletePermanentlyAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EmptyTrashAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FeatureRichSyncProxy : DispatchProxy
    {
        public FeatureRichDriveClient? Client { get; set; }
        public List<(string RemoteId, string Destination, bool FullPath)> DownloadToCalls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == $"get_{nameof(IDriveSyncService.CurrentAccount)}")
                return new DriveAccount { IsConnected = true, EmailAddress = "me@tundralab.com" };
            if (targetMethod?.Name == $"get_{nameof(IDriveSyncService.SavedAccounts)}")
                return Array.Empty<DriveAccount>();
            if (targetMethod?.Name == $"get_{nameof(IDriveSyncService.IsOnline)}")
                return true;
            if (targetMethod?.Name == nameof(IDriveSyncService.GetSyncFoldersAsync))
                return Task.FromResult<IReadOnlyList<LocalSyncFolder>>(Array.Empty<LocalSyncFolder>());
            if (targetMethod?.Name == nameof(IDriveSyncService.DownloadRemoteFileToAsync))
            {
                var remoteId = (string)args![0]!;
                var dest = (string)args[2]!;
                var full = (bool)args[3]!;
                DownloadToCalls.Add((remoteId, dest, full));
                return Task.FromResult(dest);
            }
            if (targetMethod?.Name == nameof(IDriveSyncService.GetRemotePermissionsAsync))
                return Client!.ListPermissionsAsync((string)args![0]!);
            if (targetMethod?.Name == nameof(IDriveSyncService.EnableAnyoneLinkAsync))
                return EnableLinkAsync((string)args![0]!, (bool)args[2]!);
            if (targetMethod?.Name == nameof(IDriveSyncService.DisableAnyoneLinkAsync))
                return Client!.RevokePermissionAsync((string)args![0]!, "anyoneWithLink");
            if (targetMethod?.Name == nameof(IDriveSyncService.ShareWithEmailAsync))
                return Client!.InviteUserPermissionAsync((string)args![0]!, (string)args[2]!, (bool)args[3]! ? "writer" : "reader");
            if (targetMethod?.Name == nameof(IDriveSyncService.RevokeRemotePermissionAsync))
                return Client!.RevokePermissionAsync((string)args![0]!, (string)args[2]!);
            return null;
        }

        private async Task<string> EnableLinkAsync(string remoteId, bool canEdit)
        {
            await Client!.ShareAnyoneAsync(remoteId, canEdit ? "writer" : "reader");
            return (await Client.GetShareLinkAsync(remoteId))!;
        }
    }

    private class EmptyRepoProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(ISyncStateRepository.GetSyncFoldersAsync) => Task.FromResult<IReadOnlyList<LocalSyncFolder>>(Array.Empty<LocalSyncFolder>()),
                nameof(ISyncStateRepository.SearchTrackedFilesAsync) => Task.FromResult<IReadOnlyList<TrackedFileEntry>>(Array.Empty<TrackedFileEntry>()),
                nameof(ISyncStateRepository.GetTrackedFilesByFolderAsync) => Task.FromResult<IReadOnlyList<TrackedFileEntry>>(Array.Empty<TrackedFileEntry>()),
                nameof(ISyncStateRepository.GetActivityLogsAsync) => Task.FromResult<IReadOnlyList<SyncActivityLog>>(Array.Empty<SyncActivityLog>()),
                _ => null
            };
    }

    [AvaloniaFact]
    public void SpinningAnimations_HaveCenterOrigin_AndTundraRetryGeometryIsValid()
    {
        var app = Application.Current;
        Assert.NotNull(app);
        object? retryRes = null;
        var foundRetry = app.Resources.TryGetResource("TundraRetryGeometry", null, out retryRes)
            || app.TryFindResource("TundraRetryGeometry", out retryRes);
        Assert.True(foundRetry, $"App type: {app.GetType().FullName}, Merged count: {app.Resources.MergedDictionaries.Count}");
        var retryGeometry = Assert.IsAssignableFrom<Geometry>(retryRes);
        Assert.True(retryGeometry.Bounds.Width > 8, $"Unexpected retry bounds: {retryGeometry.Bounds}");
        Assert.True(retryGeometry.Bounds.Height > 5, $"Unexpected retry bounds: {retryGeometry.Bounds}");

        var iconInlineTransform = new PathIcon
        {
            Width = 15,
            Height = 15,
            Data = retryGeometry,
            RenderTransformOrigin = RelativePoint.Parse("50%,50%"),
            RenderTransform = new RotateTransform()
        };
        iconInlineTransform.Classes.Add("m3-spinning");

        var iconStyleOnly = new PathIcon
        {
            Width = 15,
            Height = 15,
            Data = retryGeometry
        };
        iconStyleOnly.Classes.Add("m3-spinning");

        var window = new Window
        {
            Width = 240,
            Height = 120,
            Content = new StackPanel
            {
                Children = { iconInlineTransform, iconStyleOnly }
            }
        };

        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new RelativePoint(0.5, 0.5, RelativeUnit.Relative), iconInlineTransform.RenderTransformOrigin);
            Assert.Equal(new RelativePoint(0.5, 0.5, RelativeUnit.Relative), iconStyleOnly.RenderTransformOrigin);

            // Advance the headless render timer so keyframe animations tick forward.
            for (var i = 0; i < 6; i++)
            {
                Thread.Sleep(25);
                Dispatcher.UIThread.RunJobs();
            }

            static RotateTransform ExtractRotateTransform(ITransform? transform) => transform switch
            {
                RotateTransform rt => rt,
                TransformGroup tg => tg.Children.OfType<RotateTransform>().Single(),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected RenderTransform type: {transform?.GetType().FullName ?? "null"}")
            };

            var rtInline = ExtractRotateTransform(iconInlineTransform.RenderTransform);
            var rtStyleOnly = ExtractRotateTransform(iconStyleOnly.RenderTransform);
            Assert.True(rtInline.Angle > 0, $"Expected inline RotateTransform to advance, got {rtInline.Angle}.");
            Assert.True(rtStyleOnly.Angle > 0, $"Expected style RotateTransform to advance, got {rtStyleOnly.Angle}.");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task DriveSyncService_RetryFailedSyncUploadWithNullParent_ResolvesFolderAndClearsBanner()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-retry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var localFolder = Path.Combine(tempRoot, "synced");
            var subDir = Path.Combine(localFolder, "data");
            Directory.CreateDirectory(subDir);
            var dbFile = Path.Combine(subDir, "app.db");
            await File.WriteAllBytesAsync(dbFile, new byte[2048]);
            await File.WriteAllBytesAsync(Path.Combine(localFolder, "blocker.txt"), new byte[64]);

            var dbPath = Path.Combine(tempRoot, "state.db");
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var mappedFolder = new LocalSyncFolder
            {
                Id = "folder-retry-1",
                LocalPath = localFolder,
                RemoteFolderId = "remote-root-folder",
                RemoteFolderName = "Synced",
                Mode = SyncMode.TwoWay
            };
            await repo.UpsertSyncFolderAsync(mappedFolder);

            var client = new FlakySyncRetryDriveClient();
            var settings = new FakeSettingsService(tempRoot);
            var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
            using var watcher = new LocalFolderWatcherService(settings, NullLogger<LocalFolderWatcherService>.Instance);
            using var syncService = new DriveSyncService(
                client,
                repo,
                settings,
                auth,
                watcher,
                NullLogger<DriveSyncService>.Instance,
                connectivityProbe: _ => Task.FromResult(true));

            var loc = new LocalizationService();
            loc.SetLanguage("pt-BR");
            var queueVm = new TransferQueueViewModel(syncService, loc);
            syncService.TransferUpdated += (_, item) => queueVm.UpsertTransfer(item);

            // First sync fails on blocker.txt (and may fail app.db with null RemoteParentId / Sync ended with error).
            await syncService.TriggerFolderSyncAsync(mappedFolder.Id);

            Assert.True(queueVm.HasFailedTransfers);
            Assert.NotEmpty(queueVm.LastErrorMessage);

            client.FailUploads = false;
            var failedItem = queueVm.Items.FirstOrDefault(x => x.FileName == "app.db" && x.IsFailed)
                ?? queueVm.Items.First(x => x.IsFailed);

            await queueVm.RetryTransferAsync(failedItem.Id);
            await queueVm.RetryAllFailedCommand.ExecuteAsync(null);

            Assert.Equal(TransferStatus.Completed, failedItem.Status);
            Assert.Null(failedItem.ErrorMessage);
            Assert.Equal(0, queueVm.FailedCount);
            Assert.False(queueVm.HasFailedTransfers);
            Assert.Equal(string.Empty, queueVm.LastErrorMessage);

            var tracked = await repo.GetTrackedFilesByFolderAsync(mappedFolder.Id);
            Assert.Contains(tracked, t => t.RelativePath == "data/app.db");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class FlakySyncRetryDriveClient : IGoogleDriveClient
    {
        public bool IsConnected => true;
        public string ActiveAccountKey => "default";
        public bool FailUploads { get; set; } = true;
        private readonly Dictionary<string, RemoteDriveNode> _folders = new();

        public Task<DriveAccount> ConnectInteractiveAsync(string userKey = "default", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DriveAccount?> TryRestoreSessionAsync(string userKey = "default", CancellationToken cancellationToken = default) => Task.FromResult<DriveAccount?>(new DriveAccount { IsConnected = true });
        public Task<DriveAccount?> SwitchSessionAsync(string userKey, CancellationToken cancellationToken = default) => Task.FromResult<DriveAccount?>(null);
        public Task DisconnectAsync(string? userKey = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DriveAccount> GetAccountInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DriveAccount { IsConnected = true, EmailAddress = "me@tundralab.com" });
        public Task<IReadOnlyList<RemoteDriveNode>> ListChildrenAsync(string parentFolderId = "root", bool foldersOnly = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListAllRemoteFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListStarredFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListTrashedFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> SearchRemoteFilesAsync(string query, int maxResults = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<RemoteDriveNode?> GetFileMetadataAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.FromResult<RemoteDriveNode?>(null);

        public Task<RemoteDriveNode> CreateFolderAsync(string folderName, string parentFolderId, CancellationToken cancellationToken = default)
        {
            if (FailUploads)
            {
                throw new IOException("Simulated network failure creating remote folder");
            }

            return FindOrCreateFolderAsync(folderName, parentFolderId, cancellationToken);
        }

        public Task<RemoteDriveNode> FindOrCreateFolderAsync(string folderName, string parentFolderId = "root", CancellationToken cancellationToken = default)
        {
            var key = $"{parentFolderId}/{folderName}";
            if (!_folders.TryGetValue(key, out var node))
            {
                node = new RemoteDriveNode
                {
                    Id = $"dir-{_folders.Count + 1}",
                    Name = folderName,
                    IsFolder = true,
                    ParentIds = [parentFolderId]
                };
                _folders[key] = node;
            }
            return Task.FromResult(node);
        }

        public Task<RemoteDriveNode> UploadFileAsync(
            string localFilePath,
            string parentFolderId,
            string? existingRemoteFileId = null,
            Action<long>? onBytesTransferred = null,
            CancellationToken cancellationToken = default)
        {
            if (FailUploads)
            {
                throw new IOException("Simulated upload drop");
            }

            var info = new FileInfo(localFilePath);
            onBytesTransferred?.Invoke(info.Length);
            return Task.FromResult(new RemoteDriveNode
            {
                Id = existingRemoteFileId ?? $"file-{info.Name}",
                Name = info.Name,
                SizeBytes = info.Length,
                ParentIds = [parentFolderId],
                ModifiedTimeUtc = DateTimeOffset.UtcNow
            });
        }

        public Task<RemoteDriveNode> MoveFileAsync(string remoteFileId, string? destinationParentFolderId = null, string? newName = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DownloadFileAsync(string remoteFileId, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExportFileAsync(string remoteFileId, string exportMimeType, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetStarredAsync(string remoteFileId, bool starred, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TrashFileAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreFileAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeletePermanentlyAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EmptyTrashAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task GitHubAppUpdateService_IgnoresCustomInstallerAppImageAssetWhenResolvingAppImagePayload()
    {
        const string json = """
            {
              "tag_name": "v4.0.0",
              "html_url": "https://github.com/TundraLab/Custodia/releases/tag/v4.0.0",
              "assets": [
                {
                  "name": "Custodia-Installer-x86_64.AppImage",
                  "browser_download_url": "https://github.com/TundraLab/Custodia/releases/download/v4.0.0/Custodia-Installer-x86_64.AppImage",
                  "size": 50000000
                },
                {
                  "name": "Custodia-4.0.0-x86_64.AppImage",
                  "browser_download_url": "https://github.com/TundraLab/Custodia/releases/download/v4.0.0/Custodia-4.0.0-x86_64.AppImage",
                  "size": 52000000
                },
                {
                  "name": "Custodia-4.0.0-1.x86_64.rpm",
                  "browser_download_url": "https://github.com/TundraLab/Custodia/releases/download/v4.0.0/Custodia-4.0.0-1.x86_64.rpm",
                  "size": 59000000
                },
                {
                  "name": "SHA256SUMS.txt",
                  "browser_download_url": "https://github.com/TundraLab/Custodia/releases/download/v4.0.0/SHA256SUMS.txt",
                  "size": 256
                }
              ]
            }
            """;

        using var handler = new StaticJsonResponseHandler(json);
        var tempUpdates = Path.Combine(Path.GetTempPath(), $"custodia-upd-{Guid.NewGuid():N}");
        using var service = new GitHubAppUpdateService(NullLogger<GitHubAppUpdateService>.Instance, handler, tempUpdates, "3.0.5");

        var result = await service.CheckForUpdatesAsync();
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("4.0.0", result.LatestVersion);
        Assert.NotNull(result.AppImageAsset);
        Assert.Equal("Custodia-4.0.0-x86_64.AppImage", result.AppImageAsset.Name);
        Assert.NotNull(result.RpmAsset);
        Assert.Equal("Custodia-4.0.0-1.x86_64.rpm", result.RpmAsset.Name);
    }

    private sealed class StaticJsonResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task GoogleVids_UsesDownloadInsteadOfExport_ForPreviewAndDownload()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-vids-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var client = new VidsCapturingDriveClient();
            using var preview = new FilePreviewService(client);

            var previewResult = await preview.BuildPreviewAsync(
                "Minha Apresentação Vids",
                null,
                "vids-file-1",
                isGoogleWorkspaceDoc: true,
                mimeType: "application/vnd.google-apps.vid",
                pdfPage: 1,
                CancellationToken.None,
                sizeBytes: 1024);

            Assert.Equal(PreviewKind.Video, previewResult.Kind);
            Assert.EndsWith(".mp4", previewResult.LocalFilePath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, client.ExportCallCount);
            Assert.Equal(1, client.DownloadCallCount);

            var dbPath = Path.Combine(tempRoot, "state.db");
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);
            var settings = new FakeSettingsService(tempRoot);
            var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
            using var watcher = new LocalFolderWatcherService(settings, NullLogger<LocalFolderWatcherService>.Instance);
            using var sync = new DriveSyncService(
                client,
                repo,
                settings,
                auth,
                watcher,
                NullLogger<DriveSyncService>.Instance,
                connectivityProbe: _ => Task.FromResult(true));

            var destPath = Path.Combine(tempRoot, "downloads", "MeuVideo");
            var savedPath = await sync.DownloadRemoteFileToAsync(
                "vids-file-2",
                "MeuVideo",
                destPath,
                treatAsFullFilePath: true,
                sizeBytes: 2048,
                isGoogleWorkspaceDoc: true,
                mimeType: "application/vnd.google-apps.vid");

            Assert.True(File.Exists(savedPath));
            Assert.Equal(0, client.ExportCallCount);
            Assert.True(client.DownloadCallCount >= 2);
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
    public void TransferQueueViewModel_InProgressTicks_DoNotResetFilteredItemsCollection()
    {
        var loc = new LocalizationService();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, QueueRetrySyncProxy>();
        var vm = new TransferQueueViewModel(syncProxy, loc);

        var item = new TransferQueueItem
        {
            Id = "tx-1",
            FileName = "video.mp4",
            Direction = TransferDirection.Upload,
            Status = TransferStatus.InProgress,
            TotalBytes = 1000,
            TransferredBytes = 100
        };
        vm.UpsertTransfer(item);

        var resetCount = 0;
        vm.FilteredItems.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }
        };

        for (var i = 2; i <= 10; i++)
        {
            item.TransferredBytes = i * 100;
            vm.UpsertTransfer(item);
        }

        Assert.Equal(0, resetCount);
        Assert.Single(vm.FilteredItems);
        Assert.Equal(1000, vm.FilteredItems[0].TransferredBytes);
    }

    private sealed class VidsCapturingDriveClient : IGoogleDriveClient
    {
        public bool IsConnected => true;
        public string ActiveAccountKey => "default";
        public int ExportCallCount { get; private set; }
        public int DownloadCallCount { get; private set; }

        public Task<DriveAccount> ConnectInteractiveAsync(string userKey = "default", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DriveAccount?> TryRestoreSessionAsync(string userKey = "default", CancellationToken cancellationToken = default) => Task.FromResult<DriveAccount?>(new DriveAccount { IsConnected = true });
        public Task<DriveAccount?> SwitchSessionAsync(string userKey, CancellationToken cancellationToken = default) => Task.FromResult<DriveAccount?>(null);
        public Task DisconnectAsync(string? userKey = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DriveAccount> GetAccountInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DriveAccount { IsConnected = true });
        public Task<IReadOnlyList<RemoteDriveNode>> ListChildrenAsync(string parentFolderId = "root", bool foldersOnly = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListAllRemoteFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListStarredFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> ListTrashedFilesAsync(int maxResults = 200, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<IReadOnlyList<RemoteDriveNode>> SearchRemoteFilesAsync(string query, int maxResults = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteDriveNode>>([]);
        public Task<RemoteDriveNode?> GetFileMetadataAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.FromResult<RemoteDriveNode?>(null);
        public Task<RemoteDriveNode> CreateFolderAsync(string folderName, string parentFolderId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RemoteDriveNode> FindOrCreateFolderAsync(string folderName, string parentFolderId = "root", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RemoteDriveNode> UploadFileAsync(string localFilePath, string parentFolderId, string? existingRemoteFileId = null, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RemoteDriveNode> MoveFileAsync(string remoteFileId, string? destinationParentFolderId = null, string? newName = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public async Task DownloadFileAsync(string remoteFileId, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default)
        {
            DownloadCallCount++;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationLocalPath)!);
            await File.WriteAllBytesAsync(destinationLocalPath, new byte[64], cancellationToken);
            onBytesTransferred?.Invoke(64);
        }

        public Task ExportFileAsync(string remoteFileId, string exportMimeType, string destinationLocalPath, Action<long>? onBytesTransferred = null, CancellationToken cancellationToken = default)
        {
            ExportCallCount++;
            throw new InvalidOperationException("Google Vids does not support files.export.");
        }

        public Task SetStarredAsync(string remoteFileId, bool starred, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TrashFileAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreFileAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeletePermanentlyAsync(string remoteFileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EmptyTrashAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
