using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Tests;

public sealed class ViewModelBehaviorTests
{
    [Fact]
    public void TransferQueue_DistinguishesUploadDownloadAndDeleteStates()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");

        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var queueVm = new TransferQueueViewModel(syncProxy, loc);

        var oldCompleted = new TransferQueueItem
        {
            Id = "old",
            FileName = "old.txt",
            RelativePath = "old.txt",
            Direction = TransferDirection.Upload,
            Status = TransferStatus.Completed,
            TotalBytes = 1024,
            TransferredBytes = 1024,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-4)
        };
        queueVm.UpsertTransfer(oldCompleted);
        Assert.False(queueVm.Items[0].IsUploadingActive);
        Assert.True(queueVm.Items[0].IsUploadingStatic);

        var upload = new TransferQueueItem
        {
            Id = "up",
            FileName = "foto.jpg",
            RelativePath = "foto.jpg",
            LocalPath = "/tmp/foto.jpg",
            RemoteFileId = "drv_123",
            Direction = TransferDirection.Upload,
            Status = TransferStatus.InProgress,
            TotalBytes = 100,
            TransferredBytes = 50
        };
        queueVm.UpsertTransfer(upload);
        Assert.Equal(TransferDirection.Upload, queueVm.ActiveDirection);
        Assert.True(queueVm.Items[0].IsUploadingActive);
        Assert.Equal("Envio (Upload)", queueVm.Items[0].DirectionDisplay);
        Assert.Equal("/tmp", queueVm.Items[0].SubtitleText);

        queueVm.SelectedItem = queueVm.Items[0];
        Assert.True(queueVm.Items[0].IsSelected);
        Assert.True(queueVm.Items[0].HasLocalPath);
        Assert.True(queueVm.Items[0].HasRemoteFileId);

        var download = new TransferQueueItem
        {
            Id = "dl",
            FileName = "video.mp4",
            RelativePath = "sub/video.mp4",
            Direction = TransferDirection.Download,
            Status = TransferStatus.InProgress,
            TotalBytes = 100,
            TransferredBytes = 25
        };
        queueVm.UpsertTransfer(download);
        Assert.Equal(TransferDirection.Download, queueVm.ActiveDirection);
        Assert.True(queueVm.Items[0].IsDownloadingActive);
        Assert.Equal(Color.Parse("#2EA043"), ((ISolidColorBrush)queueVm.Items[0].TransferProgressBrush).Color);

        var del = new TransferQueueItem
        {
            Id = "rm",
            FileName = "apagado.txt",
            RelativePath = "apagado.txt",
            Direction = TransferDirection.Delete,
            Status = TransferStatus.InProgress
        };
        queueVm.UpsertTransfer(del);
        Assert.Equal(TransferDirection.Delete, queueVm.ActiveDirection);
        Assert.True(queueVm.Items[0].IsDeletingActive);
        Assert.Equal("Exclusão", queueVm.Items[0].DirectionDisplay);
        Assert.Equal(Color.Parse("#EF4444"), ((ISolidColorBrush)queueVm.Items[0].TransferProgressBrush).Color);
        Assert.Equal(Color.Parse("#EF4444"), ((ISolidColorBrush)queueVm.Items[0].AccentBorderBrush).Color);

        loc.SetLanguage("en-US");
        Assert.Equal("Delete", queueVm.Items[0].DirectionDisplay);
    }

    [Fact]
    public async Task MainWindow_FiltersNoOpSyncCompletedAndDispatchesMissingNotifications()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");
        var notifySpy = new RecordingNotificationService();

        var uninitVm = (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));
        typeof(MainWindowViewModel).GetField("_notificationService", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(uninitVm, notifySpy);
        typeof(MainWindowViewModel).GetField("<Loc>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(uninitVm, loc);

        await uninitVm.DispatchNotificationForLogAsync(new SyncActivityLog
        {
            Action = "SyncCompleted",
            Message = "Sincronização concluída: Docs (sem alterações).",
            TargetPath = "Docs"
        });
        Assert.Empty(notifySpy.Calls);

        await uninitVm.DispatchNotificationForLogAsync(new SyncActivityLog
        {
            Action = "SyncCompleted",
            Message = "Sincronização concluída: Docs (2 alteração(ões)).",
            TargetPath = "Docs"
        });
        Assert.Single(notifySpy.Calls);
        Assert.Equal("Sincronização concluída", notifySpy.Calls[0].Title);

        string[] expectedActions =
        [
            "AccountConnect", "AccountSwitch", "AccountDisconnect", "CredentialsRevoked", "CredentialsJsonRevoked",
            "FolderMapped", "FolderUnmapped", "ModeChanged", "Uploaded", "ApiUpload", "Downloaded",
            "ConflictRenamed", "FolderPaused", "FolderResumed", "LocalTrashed", "RemoteTrashed",
            "RemoteRestored", "RemoteDeletedPermanent", "TrashEmptied", "ManualDownload", "ManualDownloadFailed",
            "SyncError", "ApiUploadFailed"
        ];

        foreach (var act in expectedActions)
        {
            await uninitVm.DispatchNotificationForLogAsync(new SyncActivityLog { Action = act, Message = $"Evento {act}", TargetPath = "alvo" });
        }

        Assert.Equal(1 + expectedActions.Length, notifySpy.Calls.Count);

        uninitVm.IsSyncActive = true;
        uninitVm.ActiveSidebarDirection = TransferDirection.Delete;
        Assert.True(uninitVm.IsSidebarDeleting);
        Assert.False(uninitVm.IsSidebarGenericSyncing);
        Assert.Equal(Color.Parse("#EF4444"), ((ISolidColorBrush)uninitVm.SidebarSyncAccentBrush).Color);
    }

    [Fact]
    public async Task FileHubViewModel_SupportsMultiSelectAndBatchDownload()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");

        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var stubSync = (StubDriveSyncProxy)(object)syncProxy;
        var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StubDriveClientProxy>();
        var repoProxy = DispatchProxy.Create<ISyncStateRepository, StubStateRepoProxy>();
        var fileHubVm = new FileHubViewModel(repoProxy, driveProxy, syncProxy, new FilePreviewService(driveProxy), loc);

        var f1 = new FileHubItemViewModel { Name = "a.jpg", RemoteFileId = "r1", SizeBytes = 100 };
        var f2 = new FileHubItemViewModel { Name = "b.jpg", RemoteFileId = "r2", SizeBytes = 200 };
        var folder = new FileHubItemViewModel { Name = "Fotos", RemoteFileId = "r3", IsFolder = true };
        fileHubVm.Files.Add(f1);
        fileHubVm.Files.Add(f2);
        fileHubVm.Files.Add(folder);

        fileHubVm.UpdateSelectedItems([f1, f2]);
        Assert.Equal(2, fileHubVm.SelectedCount);
        Assert.True(fileHubVm.HasMultipleSelected);
        Assert.False(fileHubVm.CanPreviewSelected);
        Assert.True(fileHubVm.CanDownloadSelected);
        Assert.True(fileHubVm.CanTrashSelected);
        Assert.True(f1.IsSelected && f2.IsSelected && !folder.IsSelected);
        Assert.Equal("2 selecionado(s)", fileHubVm.SelectionCountDisplay);

        await fileHubVm.DownloadSelectedCommand.ExecuteAsync(null);
        Assert.Equal(["r1", "r2"], stubSync.DownloadedIds);
        Assert.Equal("2 arquivo(s) baixado(s).", fileHubVm.StatusSummary);
    }
}
