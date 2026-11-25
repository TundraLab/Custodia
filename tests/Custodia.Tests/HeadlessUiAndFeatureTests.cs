using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Extensions.Logging.Abstractions;
using TundraDrive.Api;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;
using TundraDrive.Desktop.ViewModels;
using TundraDrive.Desktop.Views;

[assembly: AvaloniaTestApplication(typeof(TundraDrive.Tests.CustodiaHeadlessAppBuilder))]

namespace TundraDrive.Tests;

public static class CustodiaHeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class HeadlessUiAndFeatureTests
{
    [AvaloniaFact]
    public void AllViews_LoadAndMeasureCleanly_AndAccentSwitchesLive()
    {
        var loc = new LocalizationService();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
        var repoProxy = DispatchProxy.Create<ISyncStateRepository, StubStateRepoProxy>();
        var settingsService = DispatchProxy.Create<IAppSettingsService, StubSettingsProxy>();
        var authManager = DispatchProxy.Create<IAuthCredentialManager, StubAuthProxy>();
        var apiServer = DispatchProxy.Create<IEmbeddedApiServer, StubApiServerProxy>();
        var autostart = DispatchProxy.Create<IAutostartManager, StubAutostartProxy>();
        var notifications = new RecordingNotificationService();
        var previewService = new FilePreviewService(driveProxy);
        var memoryCleaner = new MemoryCleanerService(settingsService, NullLogger<MemoryCleanerService>.Instance);

        var accountsVm = new AccountsViewModel(syncProxy, authManager, loc);
        var fileHubVm = new FileHubViewModel(repoProxy, driveProxy, syncProxy, previewService, loc, settingsService);
        var remoteBrowserVm = new RemoteBrowserViewModel(driveProxy, syncProxy, loc);
        var syncFoldersVm = new SyncFoldersViewModel(syncProxy, driveProxy, remoteBrowserVm, loc);
        var transferQueueVm = new TransferQueueViewModel(syncProxy, loc);
        var activityLogsVm = new ActivityLogsViewModel(repoProxy, loc);
        var settingsVm = new SettingsViewModel(settingsService, apiServer, autostart, loc, notifications, authManager, syncProxy, memoryCleaner);
        var aboutVm = new AboutViewModel(loc, null, settingsService, repoProxy);

        var mainVm = new MainWindowViewModel(
            syncProxy,
            apiServer,
            settingsService,
            autostart,
            loc,
            notifications,
            accountsVm,
            fileHubVm,
            syncFoldersVm,
            transferQueueVm,
            activityLogsVm,
            settingsVm,
            aboutVm);

        var window = new MainWindow { DataContext = mainVm };
        window.Measure(new Size(1120, 740));
        window.Arrange(new Rect(0, 0, 1120, 740));

        UserControl[] views =
        [
            new AccountsView { DataContext = accountsVm },
            new FileHubView { DataContext = fileHubVm },
            new SyncFoldersView { DataContext = syncFoldersVm },
            new RemoteBrowserView { DataContext = remoteBrowserVm },
            new TransferQueueView { DataContext = transferQueueVm },
            new ActivityLogsView { DataContext = activityLogsVm },
            new SettingsView { DataContext = settingsVm },
            new AboutView { DataContext = aboutVm }
        ];

        foreach (var view in views)
        {
            view.Measure(new Size(860, 700));
            view.Arrange(new Rect(0, 0, 860, 700));
        }

        // Verify SettingsView ScrollViewer includes the full inner StackPanel height (with bottom margin) in Extent
        var settingsWindow = new Window
        {
            Width = 860,
            Height = 700,
            Content = views.OfType<SettingsView>().Single()
        };
        settingsWindow.Show();
        var scrollViewer = (ScrollViewer)((SettingsView)settingsWindow.Content).Content!;
        var innerPanel = (StackPanel)scrollViewer.Content!;
        scrollViewer.Offset = new Vector(0, scrollViewer.ScrollBarMaximum.Y);
        settingsWindow.UpdateLayout();
        Assert.True(scrollViewer.Extent.Height >= innerPanel.Bounds.Height + innerPanel.Margin.Top + innerPanel.Margin.Bottom - 1);
        Assert.True(scrollViewer.ScrollBarMaximum.Y > 0);
        settingsWindow.Close();

        // Verify AboutView ScrollViewer includes the full inner StackPanel height (with bottom margin) in Extent
        var aboutWindow = new Window
        {
            Width = 1040,
            Height = 700,
            Content = views.OfType<AboutView>().Single()
        };
        aboutWindow.Show();
        var aboutScroll = (ScrollViewer)((AboutView)aboutWindow.Content).Content!;
        var aboutPanel = (StackPanel)aboutScroll.Content!;
        aboutScroll.Offset = new Vector(0, aboutScroll.ScrollBarMaximum.Y);
        aboutWindow.UpdateLayout();
        Assert.True(aboutScroll.Extent.Height >= aboutPanel.Bounds.Height + aboutPanel.Margin.Top + aboutPanel.Margin.Bottom - 1);
        Assert.True(aboutScroll.ScrollBarMaximum.Y > 0);
        aboutWindow.Close();

        ThemeAccentService.ApplyAccent(ThemeAccentService.EmeraldAccentId);
        Assert.True(Application.Current!.Resources.TryGetValue("M3Primary", out var emeraldObj));
        Assert.Equal(Color.Parse("#6DD58C"), ((ISolidColorBrush)emeraldObj!).Color);

        ThemeAccentService.ApplyAccent(ThemeAccentService.PurpleAccentId);
        Assert.True(Application.Current!.Resources.TryGetValue("M3Primary", out var purpleObj));
        Assert.Equal(Color.Parse("#D0BCFF"), ((ISolidColorBrush)purpleObj!).Color);

        ThemeAccentService.ApplyAccent(ThemeAccentService.BlueAccentId);
        Assert.True(Application.Current!.Resources.TryGetValue("M3Primary", out var blueObj));
        Assert.Equal(Color.Parse("#A8C7FA"), ((ISolidColorBrush)blueObj!).Color);
    }

    [AvaloniaFact]
    public async Task FileHubView_SeparatesFoldersAndFilesInGrid_AndHidesSelectionBarWhenIdle()
    {
        var loc = new LocalizationService();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
        var repoProxy = DispatchProxy.Create<ISyncStateRepository, StubStateRepoProxy>();
        var settingsService = DispatchProxy.Create<IAppSettingsService, StubSettingsProxy>();
        var hub = new FileHubViewModel(repoProxy, driveProxy, syncProxy, new FilePreviewService(driveProxy), loc, settingsService);

        var folder = new FileHubItemViewModel { Name = "Artigos", IsFolder = true, RemoteFileId = "f1" };
        var file = new FileHubItemViewModel { Name = "nota.pdf", IsFolder = false, RemoteFileId = "r1", SizeDisplay = "12 KB" };
        hub.Files.Add(folder);
        hub.Files.Add(file);

        Assert.Single(hub.GridFolders);
        Assert.Single(hub.GridFileItems);
        Assert.True(hub.HasGridFolders);
        Assert.True(hub.HasGridFiles);
        Assert.False(hub.ShowSelectionBar);

        var view = new FileHubView { DataContext = hub };
        view.Measure(new Size(960, 700));
        view.Arrange(new Rect(0, 0, 960, 700));

        hub.UpdateSelectedItems([folder]);
        Assert.True(hub.ShowSelectionBar);
        Assert.True(hub.CanCustomizeSelectedFolder);
        Assert.False(hub.CanPreviewSelected);

        hub.SearchText = "artigo";
        Assert.True(hub.HasSearchText);
        await hub.ClearSearchCommand.ExecuteAsync(null);
        Assert.False(hub.HasSearchText);
        Assert.Equal(string.Empty, hub.SearchText);
    }

    [AvaloniaFact]
    public void TransferQueueView_PromotesCompletedTaskAndScrollsToTop()
    {
        var loc = new LocalizationService();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var queueVm = new TransferQueueViewModel(syncProxy, loc);

        var scrollRequestedCount = 0;
        queueVm.ScrollToTopRequested += (_, _) => scrollRequestedCount++;

        var queueView = new TransferQueueView { DataContext = queueVm };
        var window = new Window
        {
            Width = 900,
            Height = 420,
            Content = queueView
        };
        window.Show();

        for (var i = 0; i < 6; i++)
        {
            queueVm.UpsertTransfer(new TransferQueueItem
            {
                Id = $"t-{i}",
                FileName = $"file-{i}.txt",
                RelativePath = $"docs/file-{i}.txt",
                Direction = TransferDirection.Upload,
                Status = TransferStatus.InProgress,
                TotalBytes = 100,
                TransferredBytes = 50
            });
        }

        window.UpdateLayout();
        var scrollViewer = queueView.FindControl<ScrollViewer>("QueueScrollViewer")!;
        Assert.True(scrollViewer.ScrollBarMaximum.Y > 0);
        scrollViewer.Offset = new Vector(0, scrollViewer.ScrollBarMaximum.Y);
        window.UpdateLayout();
        Assert.True(scrollViewer.Offset.Y > 0);

        // Complete an older item below the fold
        queueVm.UpsertTransfer(new TransferQueueItem
        {
            Id = "t-0",
            FileName = "file-0.txt",
            RelativePath = "docs/file-0.txt",
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Completed,
            TotalBytes = 100,
            TransferredBytes = 100,
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.Equal("t-0", queueVm.Items[0].Id);
        Assert.Equal(1, scrollRequestedCount);
        Assert.Equal(0, scrollViewer.Offset.Y);
        window.Close();
    }

    [Fact]
    public async Task SettingsViewModel_SwitchesSubTabs_AndPersistsAccentSelection()
    {
        var loc = new LocalizationService();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var settingsService = DispatchProxy.Create<IAppSettingsService, StubSettingsProxy>();
        var authManager = DispatchProxy.Create<IAuthCredentialManager, StubAuthProxy>();
        var apiServer = DispatchProxy.Create<IEmbeddedApiServer, StubApiServerProxy>();
        var autostart = DispatchProxy.Create<IAutostartManager, StubAutostartProxy>();
        var notifications = new RecordingNotificationService();
        var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
        var memoryCleaner = new MemoryCleanerService(settingsService, NullLogger<MemoryCleanerService>.Instance);

        var vm = new SettingsViewModel(settingsService, apiServer, autostart, loc, notifications, authManager, syncProxy, memoryCleaner);

        Assert.True(vm.IsGeneralTab);
        vm.SelectAccountsTabCommand.Execute(null);
        Assert.True(vm.IsAccountsTab);
        Assert.False(vm.IsGeneralTab);

        vm.SelectSyncTabCommand.Execute(null);
        Assert.True(vm.IsSyncTab);

        vm.SelectApiTabCommand.Execute(null);
        Assert.True(vm.IsApiTab);

        Assert.Equal(6, vm.AccentOptions.Count);
        vm.SelectedAccentIndex = 5;
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Equal(ThemeAccentService.CyanAccentId, settingsService.Current.UiAccent);
    }

    [AvaloniaFact]
    public async Task ThemeAccentService_SwitchesBetweenDarkAndLightThemesCleanly()
    {
        try
        {
            var loc = new LocalizationService();
            var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
            var settingsService = DispatchProxy.Create<IAppSettingsService, StubSettingsProxy>();
            var authManager = DispatchProxy.Create<IAuthCredentialManager, StubAuthProxy>();
            var apiServer = DispatchProxy.Create<IEmbeddedApiServer, StubApiServerProxy>();
            var autostart = DispatchProxy.Create<IAutostartManager, StubAutostartProxy>();
            var notifications = new RecordingNotificationService();
            var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
            var memoryCleaner = new MemoryCleanerService(settingsService, NullLogger<MemoryCleanerService>.Instance);

            var vm = new SettingsViewModel(settingsService, apiServer, autostart, loc, notifications, authManager, syncProxy, memoryCleaner);
            Assert.Equal(3, vm.ThemeOptions.Count);

            vm.SelectedThemeIndex = 1; // Light
            vm.SelectedAccentIndex = 0; // Blue
            await vm.SaveSettingsCommand.ExecuteAsync(null);
            Assert.Equal(ThemeAccentService.LightThemeId, settingsService.Current.UiTheme);
            Assert.True(ThemeAccentService.IsLightTheme);
            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);

            Assert.True(Application.Current.Resources.TryGetValue("M3Surface", out var surfaceObj));
            Assert.Equal(Color.Parse("#F8F9FC"), ((ISolidColorBrush)surfaceObj!).Color);
            Assert.True(Application.Current.Resources.TryGetValue("M3OnSurface", out var onSurfaceObj));
            Assert.Equal(Color.Parse("#1A1C20"), ((ISolidColorBrush)onSurfaceObj!).Color);
            Assert.True(Application.Current.Resources.TryGetValue("M3Primary", out var lightBlueObj));
            Assert.Equal(Color.Parse("#0B57D0"), ((ISolidColorBrush)lightBlueObj!).Color);

            ThemeAccentService.ApplyTheme(ThemeAccentService.LightThemeId, ThemeAccentService.EmeraldAccentId);
            Assert.True(Application.Current.Resources.TryGetValue("M3Primary", out var lightEmeraldObj));
            Assert.Equal(Color.Parse("#146C2E"), ((ISolidColorBrush)lightEmeraldObj!).Color);

            var lightBrush = (ISolidColorBrush)ThemeAccentService.ResolveThemeBrush("#6DD58C", "#146C2E");
            Assert.Equal(Color.Parse("#146C2E"), lightBrush.Color);

            ThemeAccentService.ApplyTheme(ThemeAccentService.DarkThemeId, ThemeAccentService.BlueAccentId);
            Assert.False(ThemeAccentService.IsLightTheme);
            Assert.Equal(ThemeVariant.Dark, Application.Current.RequestedThemeVariant);
            Assert.True(Application.Current.Resources.TryGetValue("M3Surface", out var darkSurfaceObj));
            Assert.Equal(Color.Parse("#111318"), ((ISolidColorBrush)darkSurfaceObj!).Color);
            Assert.True(Application.Current.Resources.TryGetValue("M3Primary", out var darkBlueObj));
            Assert.Equal(Color.Parse("#A8C7FA"), ((ISolidColorBrush)darkBlueObj!).Color);
        }
        finally
        {
            ThemeAccentService.ApplyTheme(ThemeAccentService.DarkThemeId, ThemeAccentService.BlueAccentId);
        }
    }

    [Fact]
    public async Task FileHubViewModel_RenamesAndMovesLocalFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-rename-move-{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempRoot, "Dest");
        Directory.CreateDirectory(subDir);
        var originalFile = Path.Combine(tempRoot, "old.txt");
        await File.WriteAllTextAsync(originalFile, "hello");

        try
        {
            var loc = new LocalizationService();
            var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
            var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
            var repoProxy = DispatchProxy.Create<ISyncStateRepository, StubStateRepoProxy>();
            var hub = new FileHubViewModel(repoProxy, driveProxy, syncProxy, new FilePreviewService(driveProxy), loc);

            hub.SelectedSourceFilterIndex = 1;
            hub.SelectedItem = new FileHubItemViewModel
            {
                Name = "old.txt",
                LocalFullPath = originalFile,
                HasLocalFile = true
            };

            Assert.True(hub.CanRenameSelected);
            hub.OpenRenameCommand.Execute(null);
            Assert.True(hub.IsRenameOpen);

            hub.RenameNewName = "renamed.txt";
            await hub.ConfirmRenameCommand.ExecuteAsync(null);

            var renamedFile = Path.Combine(tempRoot, "renamed.txt");
            Assert.False(File.Exists(originalFile));
            Assert.True(File.Exists(renamedFile));
            Assert.False(hub.IsRenameOpen);

            hub.Files.Clear();
            hub.Files.Add(new FileHubItemViewModel
            {
                Name = "Dest",
                IsFolder = true,
                HasLocalFile = true,
                LocalFullPath = subDir
            });

            hub.SelectedItem = new FileHubItemViewModel
            {
                Name = "renamed.txt",
                LocalFullPath = renamedFile,
                HasLocalFile = true
            };

            hub.OpenMoveCommand.Execute(null);
            Assert.True(hub.IsMoveOpen);
            Assert.Single(hub.MoveDestinations);

            await hub.ConfirmMoveCommand.ExecuteAsync(null);
            Assert.False(File.Exists(renamedFile));
            Assert.True(File.Exists(Path.Combine(subDir, "renamed.txt")));
            Assert.False(hub.IsMoveOpen);
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
    public async Task SyncFoldersAndLogs_RequireInlineConfirmationBeforeDestructiveAction()
    {
        var loc = new LocalizationService();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, RecordingRemoveSyncProxy>();
        var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
        var remoteBrowser = new RemoteBrowserViewModel(driveProxy, syncProxy, loc);
        var foldersVm = new SyncFoldersViewModel(syncProxy, driveProxy, remoteBrowser, loc);

        var model = new LocalSyncFolder { Id = "f1", LocalPath = "/tmp/docs", RemoteFolderId = "r1", RemoteFolderName = "Docs" };
        var itemVm = new SyncFolderItemViewModel(model, foldersVm, loc);
        foldersVm.Folders.Add(itemVm);
        foldersVm.SelectedFolder = itemVm;

        foldersVm.RequestRemoveSelectedFolderCommand.Execute(null);
        Assert.True(foldersVm.IsRemoveConfirmVisible);
        Assert.Empty(((RecordingRemoveSyncProxy)syncProxy).RemovedFolderIds);

        foldersVm.CancelRemoveFolderCommand.Execute(null);
        Assert.False(foldersVm.IsRemoveConfirmVisible);
        Assert.Empty(((RecordingRemoveSyncProxy)syncProxy).RemovedFolderIds);

        foldersVm.RequestRemoveSelectedFolderCommand.Execute(null);
        await foldersVm.ConfirmRemoveFolderCommand.ExecuteAsync(null);
        Assert.False(foldersVm.IsRemoveConfirmVisible);
        Assert.Equal(["f1"], ((RecordingRemoveSyncProxy)syncProxy).RemovedFolderIds);

        var repoProxy = DispatchProxy.Create<ISyncStateRepository, RecordingClearLogsRepoProxy>();
        var logsVm = new ActivityLogsViewModel(repoProxy, loc);
        logsVm.HasNoLogs = false;

        logsVm.RequestClearLogsCommand.Execute(null);
        Assert.True(logsVm.IsClearConfirmVisible);
        Assert.False(((RecordingClearLogsRepoProxy)repoProxy).Cleared);

        logsVm.CancelClearLogsCommand.Execute(null);
        Assert.False(logsVm.IsClearConfirmVisible);
        Assert.False(((RecordingClearLogsRepoProxy)repoProxy).Cleared);

        logsVm.RequestClearLogsCommand.Execute(null);
        await logsVm.ConfirmClearLogsCommand.ExecuteAsync(null);
        Assert.False(logsVm.IsClearConfirmVisible);
        Assert.True(((RecordingClearLogsRepoProxy)repoProxy).Cleared);
    }

    [AvaloniaFact]
    public async Task BatchTransfersAndAnimatedIndicators_WorkCleanly()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");

        // 1. Verify animated status flags on SyncFolderItemViewModel and render SyncFoldersView
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
        var remoteBrowserVm = new RemoteBrowserViewModel(driveProxy, syncProxy, loc);
        var foldersVm = new SyncFoldersViewModel(syncProxy, driveProxy, remoteBrowserVm, loc);
        var model = new LocalSyncFolder
        {
            Id = "sf-1",
            LocalPath = "/tmp/custodia-docs",
            RemoteFolderId = "r-docs",
            RemoteFolderName = "Docs",
            Mode = SyncMode.TwoWay,
            State = FolderSyncState.Idle,
            IsPaused = false
        };
        var itemVm = new SyncFolderItemViewModel(model, foldersVm, loc);
        Assert.True(itemVm.IsActiveMonitoring);
        Assert.False(itemVm.IsSyncing);
        Assert.False(itemVm.IsErrorState);

        model.State = FolderSyncState.Syncing;
        itemVm.UpdateFrom(model);
        Assert.True(itemVm.IsSyncing);
        Assert.False(itemVm.IsActiveMonitoring);

        model.State = FolderSyncState.Error;
        itemVm.UpdateFrom(model);
        Assert.True(itemVm.IsErrorState);

        foldersVm.Folders.Add(itemVm);
        foldersVm.HasNoFolders = false;
        var syncView = new SyncFoldersView { DataContext = foldersVm };
        syncView.Measure(new Size(960, 700));
        syncView.Arrange(new Rect(0, 0, 960, 700));

        // 2. Verify TransferQueueView renders updated direction chip without legacy pill artifacts
        var queueVm = new TransferQueueViewModel(syncProxy, loc);
        var queueItem = new TransferQueueItem
        {
            Id = "tx-1",
            FileName = "relatorio.pdf",
            RelativePath = "Docs/relatorio.pdf",
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Completed,
            TotalBytes = 2048,
            TransferredBytes = 2048,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
        queueVm.UpsertTransfer(queueItem);
        Assert.Single(queueVm.Items);
        Assert.True(queueVm.Items[0].IsCompleted);
        Assert.True(queueVm.Items[0].IsUploadDirection);
        Assert.Equal("Envio", queueVm.Items[0].DirectionDisplay);
        Assert.NotNull(queueVm.Items[0].StatusChipBackground);
        Assert.NotNull(queueVm.Items[0].StatusChipForeground);

        var queueView = new TransferQueueView { DataContext = queueVm };
        queueView.Measure(new Size(960, 700));
        queueView.Arrange(new Rect(0, 0, 960, 700));

        // 3. Verify batch multi-upload (files + subfolder) and folder/multi-file download
        var tempRoot = Path.Combine(Path.GetTempPath(), $"custodia-batch-{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempRoot, "Lote");
        Directory.CreateDirectory(subDir);
        var rootFile = Path.Combine(tempRoot, "a.txt");
        var nestedFile = Path.Combine(subDir, "b.txt");
        await File.WriteAllTextAsync(rootFile, "one");
        await File.WriteAllTextAsync(nestedFile, "two");

        try
        {
            var batchSyncProxy = DispatchProxy.Create<IDriveSyncService, RecordingBatchSyncProxy>();
            var batchDriveProxy = DispatchProxy.Create<IGoogleDriveClient, RecordingBatchDriveClientProxy>();
            var batchRepoProxy = DispatchProxy.Create<ISyncStateRepository, StubStateRepoProxy>();
            var hub = new FileHubViewModel(batchRepoProxy, batchDriveProxy, batchSyncProxy, new FilePreviewService(batchDriveProxy), loc);

            await hub.UploadDroppedFilesAsync([rootFile, subDir]);

            var recordedSync = (RecordingBatchSyncProxy)batchSyncProxy;
            var recordedDrive = (RecordingBatchDriveClientProxy)batchDriveProxy;
            Assert.Contains("Lote", recordedDrive.CreatedFolders);
            Assert.Equal(2, recordedSync.UploadedFiles.Count);

            var remoteFolderItem = new FileHubItemViewModel { Name = "Fotos", IsFolder = true, RemoteFileId = "remote-folder-1" };
            var remoteDirectFile = new FileHubItemViewModel { Name = "direto.png", IsFolder = false, RemoteFileId = "remote-direct-1", SizeBytes = 512 };
            Assert.True(remoteFolderItem.CanDownload);
            hub.UpdateSelectedItems([remoteFolderItem, remoteDirectFile]);

            await hub.DownloadSelectedCommand.ExecuteAsync(null);
            Assert.Contains("child-file-1", recordedSync.DownloadedIds);
            Assert.Contains("remote-direct-1", recordedSync.DownloadedIds);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private class RecordingBatchSyncProxy : StubDriveSyncProxy
    {
        public List<(string Path, string ParentId)> UploadedFiles { get; } = [];
        private readonly object _gate = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDriveSyncService.UploadSingleFileAsync))
            {
                var path = (string)args![0]!;
                var parentId = (string)args[1]!;
                lock (_gate)
                {
                    UploadedFiles.Add((path, parentId));
                }
                return Task.FromResult(new RemoteDriveNode { Id = Guid.NewGuid().ToString("N"), Name = Path.GetFileName(path) });
            }

            if (targetMethod?.Name == nameof(IDriveSyncService.DownloadRemoteFileAsync))
            {
                lock (_gate)
                {
                    DownloadedIds.Add((string)args![0]!);
                }
                return Task.FromResult($"/tmp/{args![1]}");
            }

            return base.Invoke(targetMethod, args);
        }
    }

    private class RecordingBatchDriveClientProxy : StubDriveClientProxy
    {
        public List<string> CreatedFolders { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IGoogleDriveClient.FindOrCreateFolderAsync))
            {
                var name = (string)args![0]!;
                CreatedFolders.Add(name);
                return Task.FromResult(new RemoteDriveNode { Id = $"dir-{name}", Name = name, IsFolder = true });
            }

            if (targetMethod?.Name == nameof(IGoogleDriveClient.ListChildrenAsync))
            {
                var parentId = (string)args![0]!;
                if (parentId == "remote-folder-1")
                {
                    IReadOnlyList<RemoteDriveNode> children =
                    [
                        new RemoteDriveNode { Id = "child-file-1", Name = "praia.jpg", IsFolder = false, SizeBytes = 1024 }
                    ];
                    return Task.FromResult(children);
                }

                return Task.FromResult<IReadOnlyList<RemoteDriveNode>>(Array.Empty<RemoteDriveNode>());
            }

            return base.Invoke(targetMethod, args);
        }
    }

    private class RecordingRemoveSyncProxy : StubDriveSyncProxy
    {
        public List<string> RemovedFolderIds { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDriveSyncService.RemoveSyncFolderAsync))
            {
                RemovedFolderIds.Add((string)args![0]!);
                return Task.CompletedTask;
            }

            return base.Invoke(targetMethod, args);
        }
    }

    private class RecordingClearLogsRepoProxy : DispatchProxy
    {
        public bool Cleared { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ISyncStateRepository.ClearActivityLogsAsync))
            {
                Cleared = true;
                return Task.CompletedTask;
            }

            return null;
        }
    }

    private class StubSettingsProxy : DispatchProxy
    {
        public AppSettings Current { get; private set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == $"get_{nameof(IAppSettingsService.Current)}")
                return Current;
            if (targetMethod?.Name == nameof(IAppSettingsService.LoadAsync))
                return Task.FromResult(Current);
            if (targetMethod?.Name == nameof(IAppSettingsService.SaveAsync) ||
                targetMethod?.Name == nameof(IAppSettingsService.SaveSilentlyAsync))
            {
                Current = (AppSettings)args![0]!;
                return Task.CompletedTask;
            }
            return null;
        }
    }

    private class StubAuthProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == $"get_{nameof(IAuthCredentialManager.CredentialsFilePath)}")
                return "/tmp/credentials.json";
            if (targetMethod?.Name == $"get_{nameof(IAuthCredentialManager.TokensDirectoryPath)}")
                return "/tmp/tokens";
            if (targetMethod?.Name == $"get_{nameof(IAuthCredentialManager.AccountsDirectoryPath)}")
                return "/tmp/accounts";
            if (targetMethod?.Name == nameof(IAuthCredentialManager.CredentialsFileExists))
                return false;
            if (targetMethod?.Name == nameof(IAuthCredentialManager.FindDownloadsCredentialsCandidates))
                return Array.Empty<DetectedCredentialsCandidate>();
            if (targetMethod?.ReturnType == typeof(string))
                return string.Empty;
            if (targetMethod?.ReturnType == typeof(Task))
                return Task.CompletedTask;
            return null;
        }
    }

    private class StubApiServerProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == $"get_{nameof(IEmbeddedApiServer.IsRunning)}")
                return false;
            if (targetMethod?.Name == $"get_{nameof(IEmbeddedApiServer.BoundAddress)}")
                return "http://127.0.0.1:5080";
            if (targetMethod?.ReturnType == typeof(Task))
                return Task.CompletedTask;
            if (targetMethod?.ReturnType == typeof(ValueTask))
                return ValueTask.CompletedTask;
            return null;
        }
    }

    private class StubAutostartProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == $"get_{nameof(IAutostartManager.IsSupported)}")
                return true;
            if (targetMethod?.Name == $"get_{nameof(IAutostartManager.DesktopEntryPath)}")
                return "/tmp/custodia.desktop";
            if (targetMethod?.Name == nameof(IAutostartManager.IsEnabled))
                return false;
            return null;
        }
    }
}
