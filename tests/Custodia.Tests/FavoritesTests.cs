using System.Reflection;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;
using TundraDrive.Desktop.ViewModels;

namespace TundraDrive.Tests;

public sealed class FavoritesTests
{
    [Fact]
    public async Task ToggleFavorite_UpdatesRemoteStarredStateAndFavoritesTab()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");

        var settings = new FavoritesSettingsStub();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StarredDriveClientProxy>();
        var stubDrive = (StarredDriveClientProxy)(object)driveProxy;
        var repoProxy = DispatchProxy.Create<ISyncStateRepository, FavoritesRepoProxy>();

        stubDrive.Nodes.Add(new RemoteDriveNode
        {
            Id = "file-1",
            Name = "Relatorio.pdf",
            MimeType = "application/pdf",
            SizeBytes = 1024,
            ParentIds = ["root"],
            Starred = false,
            ModifiedTimeUtc = DateTimeOffset.UtcNow
        });
        stubDrive.Nodes.Add(new RemoteDriveNode
        {
            Id = "folder-1",
            Name = "Projetos",
            MimeType = "application/vnd.google-apps.folder",
            IsFolder = true,
            ParentIds = ["root"],
            Starred = true,
            ModifiedTimeUtc = DateTimeOffset.UtcNow
        });

        using var preview = new FilePreviewService(driveProxy);
        using var vm = new FileHubViewModel(repoProxy, driveProxy, syncProxy, preview, loc, settings);

        await vm.RefreshAsync();
        Assert.Equal(2, vm.Files.Count);

        var fileItem = vm.Files.Single(f => f.RemoteFileId == "file-1");
        var folderItem = vm.Files.Single(f => f.RemoteFileId == "folder-1");

        Assert.False(fileItem.IsStarred);
        Assert.True(folderItem.IsStarred);

        await fileItem.ToggleFavoriteCommand!.ExecuteAsync(null);
        Assert.True(fileItem.IsStarred);
        Assert.Contains(("file-1", true), stubDrive.StarCalls);
        Assert.Contains("drive:file-1", settings.Current.FavoriteKeys);

        vm.SelectFavoritesFilterCommand.Execute(null);
        await vm.RefreshAsync();

        Assert.True(vm.IsFavoritesView);
        Assert.Equal(2, vm.Files.Count);
        Assert.Contains(vm.Files, f => f.RemoteFileId == "file-1");
        Assert.Contains(vm.Files, f => f.RemoteFileId == "folder-1");

        var starredFileInFavs = vm.Files.Single(f => f.RemoteFileId == "file-1");
        await starredFileInFavs.ToggleFavoriteCommand!.ExecuteAsync(null);

        Assert.Contains(("file-1", false), stubDrive.StarCalls);
        Assert.Single(vm.Files);
        Assert.Equal("folder-1", vm.Files[0].RemoteFileId);
    }

    [Fact]
    public async Task BatchToggleFavoriteSelected_StarsMixedSelectionThenUnstarsWhenAllStarred()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("pt-BR");

        var settings = new FavoritesSettingsStub();
        var syncProxy = DispatchProxy.Create<IDriveSyncService, StubDriveSyncProxy>();
        var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StarredDriveClientProxy>();
        var stubDrive = (StarredDriveClientProxy)(object)driveProxy;
        var repoProxy = DispatchProxy.Create<ISyncStateRepository, FavoritesRepoProxy>();

        stubDrive.Nodes.Add(new RemoteDriveNode
        {
            Id = "f1",
            Name = "Notas.txt",
            MimeType = "text/plain",
            SizeBytes = 64,
            ParentIds = ["root"],
            Starred = false,
            ModifiedTimeUtc = DateTimeOffset.UtcNow
        });
        stubDrive.Nodes.Add(new RemoteDriveNode
        {
            Id = "f2",
            Name = "Orcamento.xlsx",
            MimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            SizeBytes = 256,
            ParentIds = ["root"],
            Starred = true,
            ModifiedTimeUtc = DateTimeOffset.UtcNow
        });

        using var preview = new FilePreviewService(driveProxy);
        using var vm = new FileHubViewModel(repoProxy, driveProxy, syncProxy, preview, loc, settings);

        await vm.RefreshAsync();
        vm.UpdateSelectedItems(vm.Files);

        Assert.True(vm.CanToggleFavoriteSelected);
        Assert.Equal(loc.AddToFavoritesButton, vm.FavoriteSelectedLabel);

        await vm.ToggleFavoriteSelectedCommand.ExecuteAsync(null);
        Assert.All(vm.Files, item => Assert.True(item.IsStarred));
        Assert.Equal(loc.RemoveFromFavoritesButton, vm.FavoriteSelectedLabel);

        await vm.ToggleFavoriteSelectedCommand.ExecuteAsync(null);
        Assert.All(vm.Files, item => Assert.False(item.IsStarred));
        Assert.Equal(loc.AddToFavoritesButton, vm.FavoriteSelectedLabel);
    }

    [Fact]
    public async Task FavoritesView_OpeningStarredFolderSwitchesToDriveViewAndLocalFavoritesWorkOffline()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("en-US");

        var tempDir = Path.Combine(Path.GetTempPath(), $"custodia-fav-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var localFile = Path.Combine(tempDir, "notes.md");
        await File.WriteAllTextAsync(localFile, "# favorite note");

        try
        {
            var settings = new FavoritesSettingsStub();
            var syncProxy = DispatchProxy.Create<IDriveSyncService, FavoritesSyncProxy>();
            var stubSync = (FavoritesSyncProxy)(object)syncProxy;
            var driveProxy = DispatchProxy.Create<IGoogleDriveClient, StarredDriveClientProxy>();
            var stubDrive = (StarredDriveClientProxy)(object)driveProxy;
            var repoProxy = DispatchProxy.Create<ISyncStateRepository, FavoritesRepoProxy>();
            var stubRepo = (FavoritesRepoProxy)(object)repoProxy;

            var folder = new LocalSyncFolder
            {
                Id = "sf-1",
                LocalPath = tempDir,
                RemoteFolderId = "remote-root",
                RemoteFolderName = "Workspace"
            };
            stubSync.SyncFolders.Add(folder);
            stubRepo.SyncFolders.Add(folder);
            stubRepo.TrackedFiles.Add(new TrackedFileEntry
            {
                SyncFolderId = "sf-1",
                RelativePath = "notes.md",
                RemoteFileId = string.Empty,
                SizeBytes = 15,
                LocalModifiedAtUtc = DateTimeOffset.UtcNow
            });

            stubDrive.Nodes.Add(new RemoteDriveNode
            {
                Id = "fav-folder",
                Name = "Design",
                MimeType = "application/vnd.google-apps.folder",
                IsFolder = true,
                ParentIds = ["root"],
                Starred = true,
                ModifiedTimeUtc = DateTimeOffset.UtcNow
            });
            stubDrive.Nodes.Add(new RemoteDriveNode
            {
                Id = "inside-doc",
                Name = "Spec.pdf",
                MimeType = "application/pdf",
                SizeBytes = 512,
                ParentIds = ["fav-folder"],
                Starred = false,
                ModifiedTimeUtc = DateTimeOffset.UtcNow
            });

            using var preview = new FilePreviewService(driveProxy);
            using var vm = new FileHubViewModel(repoProxy, driveProxy, syncProxy, preview, loc, settings);

            // Opening a starred folder from Favorites switches cleanly to Drive view inside that folder.
            vm.SelectedSourceFilterIndex = 3;
            await vm.RefreshAsync();

            var favFolderItem = vm.Files.Single(f => f.RemoteFileId == "fav-folder");
            await favFolderItem.OpenActionCommand!.ExecuteAsync(null);

            Assert.True(vm.IsDriveFilter);
            Assert.Equal("fav-folder", vm.CurrentFolderId);
            Assert.Contains(vm.Files, f => f.RemoteFileId == "inside-doc");

            // Switching to local synced folder and starring a local-only tracked file persists across offline Favorites reload.
            vm.SelectedSourceFilterIndex = 1;
            await vm.RefreshAsync();
            var syncRootItem = vm.Files.Single(f => f.SyncFolderId == "sf-1");
            await syncRootItem.OpenActionCommand!.ExecuteAsync(null);

            var localNoteItem = vm.Files.Single(f => f.Name == "notes.md");
            Assert.False(localNoteItem.IsStarred);
            await localNoteItem.ToggleFavoriteCommand!.ExecuteAsync(null);
            Assert.True(localNoteItem.IsStarred);

            stubDrive.IsConnectedOverride = false;
            vm.SelectedSourceFilterIndex = 3;
            await vm.RefreshAsync();

            Assert.Single(vm.Files);
            Assert.Equal("notes.md", vm.Files[0].Name);
            Assert.True(vm.Files[0].IsStarred);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class FavoritesSettingsStub : IAppSettingsService
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

    private class FavoritesSyncProxy : DispatchProxy
    {
        public DriveAccount Account { get; } = new() { IsConnected = true, EmailAddress = "user@test.com" };
        public List<LocalSyncFolder> SyncFolders { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                $"get_{nameof(IDriveSyncService.CurrentAccount)}" => Account,
                $"get_{nameof(IDriveSyncService.SavedAccounts)}" => Array.Empty<DriveAccount>(),
                $"get_{nameof(IDriveSyncService.IsOnline)}" => true,
                $"get_{nameof(IDriveSyncService.IsSyncRunning)}" => false,
                nameof(IDriveSyncService.GetSyncFoldersAsync) => Task.FromResult<IReadOnlyList<LocalSyncFolder>>(SyncFolders.ToList()),
                nameof(IDriveSyncService.GetTransferQueueSnapshot) => Array.Empty<TransferQueueItem>(),
                nameof(IDriveSyncService.GetTransferQueueStats) => new TransferQueueStats(0, 0, 0, 0, 0),
                nameof(IDriveSyncService.RefreshAccountInfoAsync) => Task.FromResult<DriveAccount?>(Account),
                _ => null
            };
        }
    }

    private class StarredDriveClientProxy : DispatchProxy
    {
        public bool IsConnectedOverride { get; set; } = true;
        public List<RemoteDriveNode> Nodes { get; } = [];
        public List<(string Id, bool Starred)> StarCalls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_IsConnected":
                    return IsConnectedOverride;
                case nameof(IGoogleDriveClient.GetAccountInfoAsync):
                    return Task.FromResult(new DriveAccount
                    {
                        IsConnected = IsConnectedOverride,
                        EmailAddress = "user@test.com"
                    });
                case nameof(IGoogleDriveClient.ListChildrenAsync):
                {
                    var parentId = (string)args![0]!;
                    var matches = Nodes.Where(n => !n.Trashed && n.ParentIds.Contains(parentId, StringComparer.Ordinal)).ToList();
                    return Task.FromResult<IReadOnlyList<RemoteDriveNode>>(matches);
                }
                case nameof(IGoogleDriveClient.ListStarredFilesAsync):
                {
                    var matches = Nodes.Where(n => !n.Trashed && n.Starred).ToList();
                    return Task.FromResult<IReadOnlyList<RemoteDriveNode>>(matches);
                }
                case nameof(IGoogleDriveClient.ListTrashedFilesAsync):
                {
                    var matches = Nodes.Where(n => n.Trashed).ToList();
                    return Task.FromResult<IReadOnlyList<RemoteDriveNode>>(matches);
                }
                case nameof(IGoogleDriveClient.SearchRemoteFilesAsync):
                {
                    var q = ((string)args![0]!).Trim();
                    var matches = Nodes.Where(n => !n.Trashed && n.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                    return Task.FromResult<IReadOnlyList<RemoteDriveNode>>(matches);
                }
                case nameof(IGoogleDriveClient.SetStarredAsync):
                {
                    var id = (string)args![0]!;
                    var starred = (bool)args[1]!;
                    StarCalls.Add((id, starred));
                    var node = Nodes.FirstOrDefault(n => n.Id == id);
                    if (node is not null)
                    {
                        node.Starred = starred;
                    }

                    return Task.CompletedTask;
                }
            }

            return null;
        }
    }

    private class FavoritesRepoProxy : DispatchProxy
    {
        public List<LocalSyncFolder> SyncFolders { get; } = [];
        public List<TrackedFileEntry> TrackedFiles { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(ISyncStateRepository.GetSyncFoldersAsync) =>
                    Task.FromResult<IReadOnlyList<LocalSyncFolder>>(SyncFolders.ToList()),
                nameof(ISyncStateRepository.SearchTrackedFilesAsync) =>
                    Task.FromResult<IReadOnlyList<TrackedFileEntry>>(TrackedFiles.ToList()),
                nameof(ISyncStateRepository.GetTrackedFilesByFolderAsync) =>
                    Task.FromResult<IReadOnlyList<TrackedFileEntry>>(
                        TrackedFiles.Where(t => t.SyncFolderId == (string)args![0]!).ToList()),
                _ => null
            };
        }
    }
}
