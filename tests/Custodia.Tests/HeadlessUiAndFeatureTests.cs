using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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

        ThemeAccentService.ApplyAccent(ThemeAccentService.EmeraldAccentId);
        Assert.True(Application.Current!.Resources.TryGetValue("M3Primary", out var emeraldObj));
        Assert.Equal(Color.Parse("#6DD58C"), ((ISolidColorBrush)emeraldObj!).Color);

        ThemeAccentService.ApplyAccent(ThemeAccentService.BlueAccentId);
        Assert.True(Application.Current!.Resources.TryGetValue("M3Primary", out var blueObj));
        Assert.Equal(Color.Parse("#A8C7FA"), ((ISolidColorBrush)blueObj!).Color);
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

        vm.SelectedAccentIndex = 1;
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Assert.Equal(ThemeAccentService.EmeraldAccentId, settingsService.Current.UiAccent);
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
