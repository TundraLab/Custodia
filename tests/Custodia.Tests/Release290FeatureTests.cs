using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
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

public sealed class Release290FeatureTests
{
    [Fact]
    public void TransferQueue_RetainsMoreThan200Items_AndExposesErrorBanner()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");

        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var queueVm = new TransferQueueViewModel(syncProxy, loc);

        for (var i = 0; i < 260; i++)
        {
            queueVm.UpsertTransfer(new TransferQueueItem
            {
                Id = $"item-{i}",
                FileName = $"file-{i}.txt",
                RelativePath = $"file-{i}.txt",
                Direction = TransferDirection.Upload,
                Status = TransferStatus.Completed,
                TotalBytes = 128,
                TransferredBytes = 128,
                CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
        }

        Assert.Equal(260, queueVm.Items.Count);
        Assert.Equal(260, queueVm.TotalCount);
        Assert.Equal(260, queueVm.CompletedCount);

        var failedUpload = new TransferQueueItem
        {
            Id = "err-up",
            FileName = "relatorio.pdf",
            RelativePath = "relatorio.pdf",
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Failed,
            TotalBytes = 500,
            TransferredBytes = 120,
            ErrorMessage = "Quota exceeded",
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
        queueVm.UpsertTransfer(failedUpload);

        Assert.True(queueVm.HasFailedTransfers);
        Assert.True(queueVm.HasLastErrorBanner);
        Assert.Contains("relatorio.pdf", queueVm.LastErrorMessage);
        Assert.Contains("Quota exceeded", queueVm.LastErrorMessage);
        Assert.True(queueVm.Items[0].IsFailed);
        Assert.True(queueVm.Items[0].IsFailedActive);
        Assert.Contains("relatorio.pdf", queueVm.Items[0].ErrorHeadlineText);

        queueVm.DismissLastErrorBannerCommand.Execute(null);
        Assert.False(queueVm.HasLastErrorBanner);
    }

    [Fact]
    public async Task DriveSyncService_TriggerFolderSync_StopsSyncStateBeforeFinalEvent_AndSyncsEmptySubfolders()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-sync-test-{Guid.NewGuid():N}");
        var localFolder = Path.Combine(tempRoot, "Work");
        var emptySubdir = Path.Combine(localFolder, "EmptySubdir");
        var dbPath = Path.Combine(tempRoot, "test.db");
        Directory.CreateDirectory(emptySubdir);

        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var mockClient = DispatchProxy.Create<IGoogleDriveClient, MockSyncDriveClientProxy>();
            var clientState = (MockSyncDriveClientProxy)(object)mockClient;
            var settings = new TempAppSettingsService();
            var auth = new AuthCredentialManager(NullLogger<AuthCredentialManager>.Instance);
            using var watcher = new LocalFolderWatcherService(settings, NullLogger<LocalFolderWatcherService>.Instance);
            using var syncService = new DriveSyncService(
                mockClient,
                repo,
                settings,
                auth,
                watcher,
                NullLogger<DriveSyncService>.Instance);

            var runningStatesOnChange = new List<bool>();
            var loggedActions = new List<string>();
            syncService.SyncFoldersChanged += (_, _) => runningStatesOnChange.Add(syncService.IsSyncRunning);
            syncService.ActivityLogged += (_, log) => loggedActions.Add(log.Action);

            var added = await syncService.AddSyncFolderAsync(
                localFolder,
                "remote-root-folder",
                "Meus Arquivos/Work",
                SyncMode.TwoWay);

            // Wait briefly for fire-and-forget initial sync from AddSyncFolderAsync to settle if triggered
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((syncService.IsSyncRunning || !loggedActions.Contains("InitialFolderSyncCompleted"))
                   && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.Contains("EmptySubdir", clientState.CreatedFolderNames);
            Assert.Contains("InitialFolderSyncCompleted", loggedActions);
            Assert.False(syncService.IsSyncRunning);
            Assert.NotEmpty(runningStatesOnChange);
            Assert.False(runningStatesOnChange[^1]);

            loggedActions.Clear();
            runningStatesOnChange.Clear();

            await syncService.TriggerFolderSyncAsync(added.Id);
            Assert.False(syncService.IsSyncRunning);
            Assert.NotEmpty(runningStatesOnChange);
            Assert.False(runningStatesOnChange[^1]);
            Assert.DoesNotContain("InitialFolderSyncCompleted", loggedActions);
            Assert.Contains("SyncCompleted", loggedActions);
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
    public async Task AboutViewModel_OfflineUpdateCheck_StopsAnimationAndShowsScheduleSummary()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"custodia-about-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var failingHandler = new FailingHttpMessageHandler();
            using var updateService = new GitHubAppUpdateService(
                NullLogger<GitHubAppUpdateService>.Instance,
                failingHandler,
                tempDir,
                "2.9.0");

            var settings = new TempAppSettingsService();
            var loc = new LocalizationService();
            loc.SetLanguage("pt-BR");

            var aboutVm = new AboutViewModel(loc, updateService, settings)
            {
                MinimumCheckAnimationDuration = TimeSpan.FromMilliseconds(150)
            };

            Assert.Contains("Agendamento:", aboutVm.UpdateScheduleSummary);

            settings.Current.AutoCheckUpdates = false;
            await settings.SaveAsync(settings.Current);
            aboutVm.RefreshUpdateScheduleSummary();
            Assert.Contains("desativada", aboutVm.UpdateScheduleSummary, StringComparison.OrdinalIgnoreCase);

            await aboutVm.CheckForUpdatesCommand.ExecuteAsync(null);
            Assert.False(aboutVm.IsCheckingUpdates);
            Assert.True(aboutVm.IsUpdateError);
            Assert.Equal(0, aboutVm.UpdateCheckProgress);
            Assert.False(string.IsNullOrWhiteSpace(aboutVm.UpdateStatusText));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SqliteSyncStateRepository_PersistsFolderCustomization()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"custodia-db-cust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "custodia.db");

        try
        {
            var factory = new SqliteConnectionFactory(dbPath);
            await DatabaseInitializer.InitializeAsync(factory);
            var repo = new SqliteSyncStateRepository(factory);

            var folder = new LocalSyncFolder
            {
                Id = "folder-custom-1",
                AccountId = "default",
                LocalPath = Path.Combine(tempDir, "Docs"),
                RemoteFolderId = "rem-1",
                RemoteFolderName = "Meus Arquivos/Docs",
                Mode = SyncMode.TwoWay,
                CustomAlias = "Projetos Ativos",
                CustomColorHex = "#10B981",
                CustomIconKey = "code"
            };

            await repo.UpsertSyncFolderAsync(folder);

            var loaded = await repo.GetSyncFolderByIdAsync("folder-custom-1");
            Assert.NotNull(loaded);
            Assert.Equal("Projetos Ativos", loaded.CustomAlias);
            Assert.Equal("#10B981", loaded.CustomColorHex);
            Assert.Equal("code", loaded.CustomIconKey);
            Assert.Equal("Projetos Ativos", loaded.EffectiveDisplayName);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void NotificationSoundService_BuildWavBytes_ProducesValidRiffWaveForAllPresets()
    {
        foreach (var preset in NotificationSoundService.Presets)
        {
            var wav = NotificationSoundService.BuildWavBytes(preset.Id);
            AssertValidWave(wav);
        }

        var errorWav = NotificationSoundService.BuildWavBytes("error");
        AssertValidWave(errorWav);
    }

    private static void AssertValidWave(byte[] wav)
    {
        Assert.True(wav.Length > 44);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Contains(wav.Skip(44), b => b != 0);
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network unavailable");
    }

    private sealed class TempAppSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public event EventHandler<AppSettings>? SettingsChanged;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

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

    private class MockSyncDriveClientProxy : DispatchProxy
    {
        public List<string> CreatedFolderNames { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_IsConnected":
                    return true;
                case "get_ActiveAccountKey":
                    return "default";
                case nameof(IGoogleDriveClient.ListChildrenAsync):
                    return Task.FromResult<IReadOnlyList<RemoteDriveNode>>(Array.Empty<RemoteDriveNode>());
                case nameof(IGoogleDriveClient.CreateFolderAsync):
                case nameof(IGoogleDriveClient.FindOrCreateFolderAsync):
                {
                    var name = (string)args![0]!;
                    CreatedFolderNames.Add(name);
                    return Task.FromResult(new RemoteDriveNode
                    {
                        Id = $"drv-{Guid.NewGuid():N}",
                        Name = name,
                        IsFolder = true
                    });
                }
                case nameof(IGoogleDriveClient.GetAccountInfoAsync):
                    return Task.FromResult(new DriveAccount
                    {
                        Id = "default",
                        IsConnected = true,
                        EmailAddress = "test@example.com"
                    });
                case nameof(IGoogleDriveClient.TrySendGmailAlertAsync):
                    return Task.FromResult(true);
                default:
                    return null;
            }
        }
    }
}
