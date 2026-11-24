using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Converters;
using TundraDrive.Desktop.Localization;

namespace TundraDrive.Desktop.ViewModels;

public sealed class RemoteBreadcrumbItem
{
    public string FolderId { get; }
    public string Name { get; }
    public IRelayCommand SelectCommand { get; }

    public RemoteBreadcrumbItem(string folderId, string name, Func<RemoteBreadcrumbItem, Task> onSelect)
    {
        FolderId = folderId;
        Name = name;
        SelectCommand = new AsyncRelayCommand(() => onSelect(this));
    }
}

public sealed class RemoteNodeItemViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsFolder { get; init; }
    public bool IsGoogleWorkspaceDoc { get; init; }
    public string TypeDisplay { get; init; } = string.Empty;
    public string SizeDisplay { get; init; } = string.Empty;
    public string ModifiedDisplay { get; init; } = string.Empty;
    public string? Md5Checksum { get; init; }
    public string OpenLabel { get; init; } = string.Empty;
    public IAsyncRelayCommand? OpenCommand { get; init; }
    public string DownloadLabel { get; init; } = string.Empty;
    public IAsyncRelayCommand? DownloadCommand { get; init; }
    public string OpenInDriveLabel { get; init; } = string.Empty;
    public IAsyncRelayCommand? OpenInDriveCommand { get; init; }
    public string SyncThisFolderLabel { get; init; } = string.Empty;
    public IAsyncRelayCommand? SyncThisFolderCommand { get; init; }
    public string TrashLabel { get; init; } = string.Empty;
    public IAsyncRelayCommand? TrashCommand { get; init; }
    public string CopyNameLabel { get; init; } = string.Empty;
    public IAsyncRelayCommand? CopyNameCommand { get; init; }
    public string CopyLinkLabel { get; init; } = string.Empty;
    public IAsyncRelayCommand? CopyLinkCommand { get; init; }
}

public sealed partial class RemoteBrowserViewModel : ViewModelBase
{
    private readonly IGoogleDriveClient _driveClient;
    private readonly IDriveSyncService _syncService;
    private IReadOnlyList<RemoteDriveNode> _lastNodes = [];

    public ILocalizationService Loc { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedNode))]
    [NotifyPropertyChangedFor(nameof(CanOpenSelectedFolder))]
    [NotifyPropertyChangedFor(nameof(CanDownloadSelectedFile))]
    private RemoteNodeItemViewModel? _selectedNode;

    [ObservableProperty]
    private string _currentFolderId = "root";

    [ObservableProperty]
    private string _currentFolderName = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool CanNavigateBack => Breadcrumbs.Count > 1;
    public bool HasSelectedNode => SelectedNode is not null;
    public bool CanOpenSelectedFolder => SelectedNode is { IsFolder: true };
    public bool CanDownloadSelectedFile => SelectedNode is { IsFolder: false };

    [ObservableProperty]
    private bool _isCreateFolderOpen;

    [ObservableProperty]
    private string _newFolderName = string.Empty;

    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Func<string, Task>? CopyTextCallback { get; set; }

    public ObservableCollection<RemoteBreadcrumbItem> Breadcrumbs { get; } = [];
    public ObservableCollection<RemoteNodeItemViewModel> Nodes { get; } = [];

    public event Func<string, string, Task>? RemoteFolderSelectedForSync;
    public event Func<string, string, Task>? RemoteParentSelectedForAutoSubfolderSync;

    public RemoteBrowserViewModel(
        IGoogleDriveClient driveClient,
        IDriveSyncService syncService,
        ILocalizationService localization)
    {
        _driveClient = driveClient;
        _syncService = syncService;
        Loc = localization;
        CurrentFolderName = Loc.Text("MyDriveLabel");
        StatusMessage = Loc.Text("BrowserDefaultStatus");
        Breadcrumbs.Add(new RemoteBreadcrumbItem("root", Loc.Text("MyDriveLabel"), NavigateToBreadcrumbAsync));

        Loc.LanguageChanged += (_, _) =>
        {
            if (Breadcrumbs.Count > 0 && Breadcrumbs[0].FolderId == "root")
            {
                Breadcrumbs[0] = new RemoteBreadcrumbItem("root", Loc.Text("MyDriveLabel"), NavigateToBreadcrumbAsync);
                if (CurrentFolderId == "root")
                {
                    CurrentFolderName = Loc.Text("MyDriveLabel");
                }
            }
            PopulateNodes(_lastNodes);
        };
    }

    [RelayCommand]
    public async Task LoadCurrentFolderAsync()
    {
        await LoadCurrentFolderInternalAsync();
    }

    private async Task<bool> LoadCurrentFolderInternalAsync()
    {
        if (!_driveClient.IsConnected)
        {
            ClearWhenDisconnected();
            return false;
        }

        if (!_syncService.IsOnline)
        {
            ClearWhenOffline();
            return false;
        }

        IsLoading = true;
        StatusMessage = Loc.Format("BrowserLoadingFmt", CurrentFolderName);
        try
        {
            var children = await _driveClient.ListChildrenAsync(CurrentFolderId, foldersOnly: false);
            PopulateNodes(children);
            StatusMessage = Loc.Format("BrowserCountFmt", Nodes.Count, CurrentFolderName);
            return true;
        }
        catch (Exception ex)
        {
            _syncService.ReportNetworkFailure();
            StatusMessage = Loc.Format("BrowserListErrorFmt", ex.Message);
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToRootAsync()
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new RemoteBreadcrumbItem("root", Loc.Text("MyDriveLabel"), NavigateToBreadcrumbAsync));
        CurrentFolderId = "root";
        CurrentFolderName = Loc.Text("MyDriveLabel");
        OnPropertyChanged(nameof(CanNavigateBack));
        await LoadCurrentFolderAsync();
    }

    [RelayCommand]
    public async Task NavigateBackAsync()
    {
        if (!CanNavigateBack || Breadcrumbs.Count <= 1)
        {
            return;
        }

        Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        var previous = Breadcrumbs[^1];
        CurrentFolderId = previous.FolderId;
        CurrentFolderName = previous.Name;
        OnPropertyChanged(nameof(CanNavigateBack));
        await LoadCurrentFolderAsync();
    }

    [RelayCommand]
    private async Task DownloadSelectedNodeAsync()
    {
        if (SelectedNode?.DownloadCommand is not null)
        {
            await SelectedNode.DownloadCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task TrashSelectedNodeAsync()
    {
        if (SelectedNode?.TrashCommand is not null)
        {
            await SelectedNode.TrashCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task NavigateToBreadcrumbAsync(RemoteBreadcrumbItem? item)
    {
        if (item is null)
        {
            return;
        }

        var index = Breadcrumbs.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        while (Breadcrumbs.Count - 1 > index)
        {
            Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        }

        CurrentFolderId = item.FolderId;
        CurrentFolderName = item.Name;
        OnPropertyChanged(nameof(CanNavigateBack));
        await LoadCurrentFolderAsync();
    }

    public void ClearWhenDisconnected() => ResetBrowserState(Loc.Text("BrowserConnectHint"));

    public void ClearWhenOffline() => ResetBrowserState(Loc.Text("OfflineModeNotice"));

    private void ResetBrowserState(string statusMessage)
    {
        _lastNodes = [];
        Nodes.Clear();
        SelectedNode = null;
        SearchQuery = string.Empty;
        Breadcrumbs.Clear();
        CurrentFolderId = "root";
        CurrentFolderName = Loc.Text("MyDriveLabel");
        Breadcrumbs.Add(new RemoteBreadcrumbItem("root", CurrentFolderName, NavigateToBreadcrumbAsync));
        OnPropertyChanged(nameof(CanNavigateBack));
        IsLoading = false;
        StatusMessage = statusMessage;
    }

    [RelayCommand]
    private async Task OpenNodeAsync(RemoteNodeItemViewModel? node)
    {
        var target = node ?? SelectedNode;
        if (target is null || !target.IsFolder)
        {
            return;
        }

        await OpenNodeByIdAsync(target.Id, target.Name);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!_driveClient.IsConnected)
        {
            StatusMessage = Loc.Text("BrowserSearchAuthHint");
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            await LoadCurrentFolderAsync();
            return;
        }

        IsLoading = true;
        StatusMessage = Loc.Format("BrowserSearchingFmt", SearchQuery);
        try
        {
            var results = await _driveClient.SearchRemoteFilesAsync(SearchQuery.Trim());
            PopulateNodes(results);
            StatusMessage = Loc.Format("BrowserSearchResultsFmt", Nodes.Count, SearchQuery);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("BrowserSearchErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SyncSelectedOrCurrentFolderAsync()
    {
        var targetId = SelectedNode is { IsFolder: true } ? SelectedNode.Id : CurrentFolderId;
        var targetName = SelectedNode is { IsFolder: true }
            ? BuildBreadcrumbRemotePath(SelectedNode.Name)
            : BuildBreadcrumbRemotePath(null);

        if (RemoteFolderSelectedForSync is not null)
        {
            await RemoteFolderSelectedForSync.Invoke(targetId, targetName);
        }
    }

    [RelayCommand]
    private void ToggleCreateFolder()
    {
        if (!_driveClient.IsConnected || !_syncService.IsOnline)
        {
            return;
        }

        IsCreateFolderOpen = !IsCreateFolderOpen;
        if (IsCreateFolderOpen)
        {
            NewFolderName = string.Empty;
        }
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        if (!_driveClient.IsConnected || !_syncService.IsOnline)
        {
            return;
        }

        var trimmed = (NewFolderName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            StatusMessage = Loc.Text("NewFolderNameRequired");
            return;
        }

        IsLoading = true;
        try
        {
            var created = await _driveClient.CreateFolderAsync(trimmed, CurrentFolderId);
            IsCreateFolderOpen = false;
            NewFolderName = string.Empty;
            await LoadCurrentFolderInternalAsync();
            StatusMessage = Loc.Format("FolderCreatedFmt", created.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SyncLocalFoldersIntoCurrentAsync()
    {
        var targetId = SelectedNode is { IsFolder: true }
            ? SelectedNode.Id
            : CurrentFolderId;
        var targetPath = SelectedNode is { IsFolder: true }
            ? BuildBreadcrumbRemotePath(SelectedNode.Name)
            : BuildBreadcrumbRemotePath(null);

        if (RemoteParentSelectedForAutoSubfolderSync is not null)
        {
            await RemoteParentSelectedForAutoSubfolderSync.Invoke(targetId, targetPath);
        }
    }

    private string BuildBreadcrumbRemotePath(string? childFolderName)
    {
        var basePath = Breadcrumbs.Count > 0
            ? string.Join("/", Breadcrumbs.Select(b => b.Name))
            : CurrentFolderName;

        return string.IsNullOrWhiteSpace(childFolderName)
            ? basePath
            : $"{basePath}/{childFolderName}";
    }

    private void PopulateNodes(IReadOnlyList<RemoteDriveNode> items)
    {
        _lastNodes = items;
        Nodes.Clear();
        var culture = CultureInfo.GetCultureInfo(Loc.CurrentLanguage);

        foreach (var node in items)
        {
            var typeLabel = node.IsFolder
                ? Loc.Text("NodeTypeFolder")
                : node.IsGoogleWorkspaceDoc
                    ? Loc.Text("NodeTypeGoogleDoc")
                    : Loc.Text("NodeTypeFile");

            var nodeId = node.Id;
            var nodeName = node.Name;
            var isFolder = node.IsFolder;

            Nodes.Add(new RemoteNodeItemViewModel
            {
                Id = node.Id,
                Name = node.Name,
                IsFolder = node.IsFolder,
                IsGoogleWorkspaceDoc = node.IsGoogleWorkspaceDoc,
                TypeDisplay = typeLabel,
                SizeDisplay = node.IsFolder || node.IsGoogleWorkspaceDoc
                    ? "—"
                    : UiConverters.FormatBytes(node.SizeBytes),
                ModifiedDisplay = node.ModifiedTimeUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", culture),
                Md5Checksum = node.Md5Checksum,
                OpenLabel = Loc.Text("OpenFolderButton"),
                OpenCommand = node.IsFolder
                    ? new AsyncRelayCommand(() => OpenNodeByIdAsync(nodeId, nodeName))
                    : null,
                DownloadLabel = Loc.Text("DownloadButton"),
                DownloadCommand = !node.IsFolder
                    ? new AsyncRelayCommand(() => DownloadNodeAsync(node))
                    : null,
                OpenInDriveLabel = Loc.Text("OpenInDriveButton"),
                OpenInDriveCommand = new AsyncRelayCommand(() => OpenInDriveAsync(nodeId, isFolder)),
                SyncThisFolderLabel = Loc.Text("SyncThisFolderMenu"),
                SyncThisFolderCommand = isFolder
                    ? new AsyncRelayCommand(() => SyncFolderAsync(nodeId, BuildBreadcrumbRemotePath(nodeName)))
                    : null,
                TrashLabel = Loc.Text("MoveToTrashButton"),
                TrashCommand = new AsyncRelayCommand(() => TrashNodeAsync(nodeId, nodeName)),
                CopyNameLabel = Loc.Text("CopyNameMenu"),
                CopyNameCommand = new AsyncRelayCommand(() => CopyToClipboardAsync(nodeName)),
                CopyLinkLabel = Loc.Text("CopyDriveLinkMenu"),
                CopyLinkCommand = new AsyncRelayCommand(() => CopyToClipboardAsync(isFolder
                    ? $"https://drive.google.com/drive/folders/{nodeId}"
                    : $"https://drive.google.com/file/d/{nodeId}/view"))
            });
        }
    }

    private async Task SyncFolderAsync(string folderId, string folderName)
    {
        if (RemoteFolderSelectedForSync is not null)
        {
            await RemoteFolderSelectedForSync.Invoke(folderId, folderName);
        }
    }

    private async Task OpenInDriveAsync(string remoteId, bool isFolder)
    {
        if (OpenExternalUriCallback is null || string.IsNullOrWhiteSpace(remoteId))
        {
            return;
        }

        var url = isFolder
            ? $"https://drive.google.com/drive/folders/{remoteId}"
            : $"https://drive.google.com/file/d/{remoteId}/view";
        await OpenExternalUriCallback.Invoke(new Uri(url));
    }

    private async Task TrashNodeAsync(string remoteId, string name)
    {
        if (!_driveClient.IsConnected || string.IsNullOrWhiteSpace(remoteId))
        {
            return;
        }

        IsLoading = true;
        try
        {
            await _driveClient.TrashFileAsync(remoteId);
            await Task.Delay(750);
            await _syncService.RefreshAccountInfoAsync();
            await LoadCurrentFolderAsync();
            StatusMessage = Loc.Format("ItemMovedToTrashFmt", name);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CopyToClipboardAsync(string text)
    {
        if (CopyTextCallback is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        await CopyTextCallback.Invoke(text);
        StatusMessage = Loc.Text("CopiedFeedbackFmt");
    }

    private async Task DownloadNodeAsync(RemoteDriveNode node)
    {
        StatusMessage = Loc.Format("DownloadingFileFmt", node.Name);
        try
        {
            var savedPath = await _syncService.DownloadRemoteFileAsync(
                node.Id,
                node.Name,
                node.SizeBytes,
                node.IsGoogleWorkspaceDoc,
                node.MimeType);
            StatusMessage = Loc.Format("DownloadedToFmt", savedPath);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("FileHubErrorFmt", ex.Message);
        }
    }

    private async Task OpenNodeByIdAsync(string folderId, string folderName)
    {
        if (!_driveClient.IsConnected ||
            !_syncService.IsOnline ||
            string.IsNullOrWhiteSpace(folderId) ||
            string.Equals(CurrentFolderId, folderId, StringComparison.Ordinal))
        {
            return;
        }

        var prevFolderId = CurrentFolderId;
        var prevFolderName = CurrentFolderName;
        var prevCount = Breadcrumbs.Count;

        if (!string.Equals(Breadcrumbs.LastOrDefault()?.FolderId, folderId, StringComparison.Ordinal))
        {
            Breadcrumbs.Add(new RemoteBreadcrumbItem(folderId, folderName, NavigateToBreadcrumbAsync));
        }

        CurrentFolderId = folderId;
        CurrentFolderName = folderName;
        OnPropertyChanged(nameof(CanNavigateBack));

        var ok = await LoadCurrentFolderInternalAsync();
        if (!ok)
        {
            while (Breadcrumbs.Count > prevCount)
            {
                Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
            }

            CurrentFolderId = prevFolderId;
            CurrentFolderName = prevFolderName;
            OnPropertyChanged(nameof(CanNavigateBack));
        }
    }
}
