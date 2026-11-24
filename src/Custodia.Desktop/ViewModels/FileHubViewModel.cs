using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Converters;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;

namespace TundraDrive.Desktop.ViewModels;

public sealed class FileHubBreadcrumbItem
{
    public string FolderId { get; }
    public string Name { get; }
    public IRelayCommand SelectCommand { get; }

    public FileHubBreadcrumbItem(string folderId, string name, Func<FileHubBreadcrumbItem, Task> onSelect)
    {
        FolderId = folderId;
        Name = name;
        SelectCommand = new AsyncRelayCommand(() => onSelect(this));
    }
}

public sealed partial class FileHubItemViewModel : ObservableObject, IDisposable
{
    internal static readonly IBrush DefaultMutedBrush = SolidColorBrush.Parse("#9AA4B2");

    private bool _isDisposed;

    public string Name { get; init; } = string.Empty;
    public string PathDisplay { get; init; } = string.Empty;
    public string OriginDisplay { get; init; } = string.Empty;
    public string SizeDisplay { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string ModifiedDisplay { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public bool IsFolder { get; init; }
    public bool IsVideoFile { get; init; }
    public bool IsGoogleWorkspaceDoc { get; init; }
    public bool HasLocalFile { get; init; }
    public bool IsInTrash { get; init; }
    public string? LocalFullPath { get; init; }
    public string RemoteFileId { get; init; } = string.Empty;
    public string ActionLabel { get; init; } = string.Empty;
    public bool CanDownload => !IsFolder && !string.IsNullOrWhiteSpace(RemoteFileId);
    public bool CanPreview => !IsFolder;
    public bool CanMoveToTrash => !IsInTrash && !string.IsNullOrWhiteSpace(RemoteFileId);
    public bool CanCopyDriveLink => !IsInTrash && !string.IsNullOrWhiteSpace(RemoteFileId);
    public bool CanOpenNormal => !IsInTrash;
    public string DownloadLabel { get; init; } = string.Empty;
    public string PreviewLabel { get; init; } = string.Empty;
    public string OpenInDriveLabel { get; init; } = string.Empty;
    public string TrashLabel { get; init; } = string.Empty;
    public string RestoreLabel { get; init; } = string.Empty;
    public string DeletePermanentlyLabel { get; init; } = string.Empty;
    public string CopyNameLabel { get; init; } = string.Empty;
    public string CopyLinkLabel { get; init; } = string.Empty;
    public string FileIconData { get; init; } = string.Empty;
    public IBrush FileIconBrush { get; init; } = DefaultMutedBrush;
    public IBrush OriginBrush { get; init; } = DefaultMutedBrush;
    public IAsyncRelayCommand? OpenActionCommand { get; init; }
    public IAsyncRelayCommand? DownloadCommand { get; init; }
    public IAsyncRelayCommand? PreviewCommand { get; set; }
    public IAsyncRelayCommand? OpenInDriveCommand { get; init; }
    public IAsyncRelayCommand? TrashCommand { get; init; }
    public IAsyncRelayCommand? RestoreCommand { get; init; }
    public IAsyncRelayCommand? DeletePermanentlyCommand { get; init; }
    public IAsyncRelayCommand? CopyNameCommand { get; init; }
    public IAsyncRelayCommand? CopyDriveLinkCommand { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    private Bitmap? _thumbnailBitmap;

    public bool HasThumbnail => ThumbnailBitmap is not null;
    public bool IsDisposed => _isDisposed;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        var prev = ThumbnailBitmap;
        ThumbnailBitmap = null;
        prev?.Dispose();
    }
}

public sealed partial class FileHubViewModel : ViewModelBase, IDisposable
{
    private static readonly IBrush BrushMutedGray = FileHubItemViewModel.DefaultMutedBrush;
    private static readonly IBrush BrushFolderBlue = SolidColorBrush.Parse("#60A5FA");
    private static readonly IBrush BrushSyncedGreen = SolidColorBrush.Parse("#2EA043");
    private static readonly IBrush BrushTrashSlate = SolidColorBrush.Parse("#94A3B8");
    private static readonly IBrush BrushSheetGreen = SolidColorBrush.Parse("#22C55E");
    private static readonly IBrush BrushPdfRed = SolidColorBrush.Parse("#EF4444");
    private static readonly IBrush BrushDocBlue = SolidColorBrush.Parse("#3B82F6");
    private static readonly IBrush BrushSlideAmber = SolidColorBrush.Parse("#F59E0B");
    private static readonly IBrush BrushVideoCyan = SolidColorBrush.Parse("#38BDF8");
    private static readonly IBrush BrushAudioPurple = SolidColorBrush.Parse("#A855F7");
    private static readonly IBrush BrushImagePink = SolidColorBrush.Parse("#EC4899");
    private const string GoogleOneStorageUrl = "https://one.google.com/storage";

    private const string FolderIconSvg = "M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z";
    private const string SheetIconSvg = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zM9 17H7v-2h2v2zm0-4H7v-2h2v2zm0-4H7V7h2v2zm8 8h-6v-2h6v2zm0-4h-6v-2h6v2zm0-4h-6V7h6v2z";
    private const string DocIconSvg = "M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z";
    private const string PdfIconSvg = "M20 2H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-8.5 7.5c0 .83-.67 1.5-1.5 1.5H9v2H7.5V7H10c.83 0 1.5.67 1.5 1.5v1zm5 2c0 .83-.67 1.5-1.5 1.5h-2.5V7H15c.83 0 1.5.67 1.5 1.5v3zm4-3H19v1h1.5V11H19v2h-1.5V7h3v1.5zM4 6H2v14c0 1.1.9 2 2 2h14v-2H4V6z";
    private const string ImageIconSvg = "M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z";
    private const string VideoIconSvg = "M17 10.5V7c0-.55-.45-1-1-1H4c-.55 0-1 .45-1 1v10c0 .55.45 1 1 1h12c.55 0 1-.45 1-1v-3.5l4 4v-11l-4 4z";
    private const string AudioIconSvg = "M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z";

    private readonly ISyncStateRepository _repository;
    private readonly IGoogleDriveClient _driveClient;
    private readonly IDriveSyncService _syncService;
    private readonly FilePreviewService _previewService;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _thumbnailCts;
    private CancellationTokenSource? _refreshCts;
    private FileHubItemViewModel? _previewedItem;
    private string _previewLocalFilePath = string.Empty;
    private DriveAccount _lastAccount = new();
    private const int ItemsPerPage = 48;
    private const string LocalRootCrumbId = "__local_root__";
    private const string LocalFolderCrumbPrefix = "local:";
    private readonly List<FileHubBreadcrumbItem> _driveBreadcrumbs = [];
    private readonly List<FileHubBreadcrumbItem> _localBreadcrumbs = [];
    private string _driveFolderId = "root";
    private string _driveFolderName = string.Empty;
    private string? _localSyncFolderId;
    private string _localSubPath = string.Empty;
    private IReadOnlyList<Func<FileHubItemViewModel>> _allPageFactories = [];
    private string _activePageContextLabel = string.Empty;

    public ILocalizationService Loc { get; }

    [ObservableProperty]
    private int _selectedSourceFilterIndex;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveListFiles))]
    [NotifyPropertyChangedFor(nameof(ActiveGridFiles))]
    private bool _isGridLayout = true;

    public ObservableCollection<FileHubItemViewModel>? ActiveListFiles => IsGridLayout ? null : Files;
    public ObservableCollection<FileHubItemViewModel>? ActiveGridFiles => IsGridLayout ? Files : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviousPage))]
    [NotifyPropertyChangedFor(nameof(CanNextPage))]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultiplePages))]
    [NotifyPropertyChangedFor(nameof(CanNextPage))]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    private int _totalPages = 1;

    public bool HasMultiplePages => TotalPages > 1;
    public bool CanPreviousPage => CurrentPage > 1;
    public bool CanNextPage => CurrentPage < TotalPages;
    public string PageDisplay => $"{CurrentPage} / {TotalPages}";

    [ObservableProperty]
    private bool _isFolderEmpty;

    [ObservableProperty]
    private string _currentFolderId = "root";

    [ObservableProperty]
    private string _currentFolderName = string.Empty;

    [ObservableProperty]
    private string _statusSummary = string.Empty;

    [ObservableProperty]
    private FileHubItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _isAccountConnected;

    [ObservableProperty]
    private string _quotaShortText = string.Empty;

    [ObservableProperty]
    private string _quotaBreakdownTooltip = string.Empty;

    [ObservableProperty]
    private double _driveBarWidth;

    [ObservableProperty]
    private double _otherBarWidth;

    [ObservableProperty]
    private double _trashBarWidth;

    [ObservableProperty]
    private bool _isPreviewVisible;

    [ObservableProperty]
    private bool _isPreviewLoading;

    [ObservableProperty]
    private string _previewFileName = string.Empty;

    [ObservableProperty]
    private string _previewMetaText = string.Empty;

    [ObservableProperty]
    private string _previewMessage = string.Empty;

    [ObservableProperty]
    private Bitmap? _previewBitmap;

    [ObservableProperty]
    private string? _previewTextContent;

    [ObservableProperty]
    private PreviewKind _currentPreviewKind = PreviewKind.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviousPdfPage))]
    [NotifyPropertyChangedFor(nameof(CanNextPdfPage))]
    private int _currentPdfPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PdfPageDisplay))]
    [NotifyPropertyChangedFor(nameof(CanNextPdfPage))]
    private int _totalPdfPages = 1;

    [ObservableProperty]
    private bool _isAudioPlaying;

    [ObservableProperty]
    private string _trashQuotaBadge = string.Empty;

    [ObservableProperty]
    private bool _isEmptyTrashConfirmVisible;

    [ObservableProperty]
    private bool _isCreateFolderOpen;

    [ObservableProperty]
    private string _newFolderName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTransferErrorBanner))]
    private string _transferErrorBannerText = string.Empty;

    public bool HasTransferErrorBanner => !string.IsNullOrWhiteSpace(TransferErrorBannerText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFile))]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    [NotifyPropertyChangedFor(nameof(HasMultipleSelected))]
    [NotifyPropertyChangedFor(nameof(CanPreviewSelected))]
    [NotifyPropertyChangedFor(nameof(CanDownloadSelected))]
    [NotifyPropertyChangedFor(nameof(CanOpenSelectedInDrive))]
    [NotifyPropertyChangedFor(nameof(CanTrashSelected))]
    [NotifyPropertyChangedFor(nameof(CanRestoreSelected))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelectedPermanently))]
    [NotifyPropertyChangedFor(nameof(SelectionCountDisplay))]
    private int _selectedCount;

    public bool CanPreviousPdfPage => CurrentPdfPage > 1;
    public bool CanNextPdfPage => CurrentPdfPage < TotalPdfPages;
    public bool IsDriveFilter => SelectedSourceFilterIndex == 0;
    public bool IsLocalFilter => SelectedSourceFilterIndex == 1;
    public bool IsTrashView => SelectedSourceFilterIndex == 2;
    public string TrashTabDisplay => string.IsNullOrWhiteSpace(TrashQuotaBadge)
        ? Loc.TrashTabButton
        : $"{Loc.TrashTabButton} ({TrashQuotaBadge})";
    public string RootBreadcrumbLabel => IsLocalFilter ? Loc.Text("FileHubFilterLocal") : Loc.Text("MyDriveLabel");
    public bool CanNavigateBack => (SelectedSourceFilterIndex == 0 || SelectedSourceFilterIndex == 1) && Breadcrumbs.Count > 1;
    public bool HasSelectedFile => GetEffectiveSelectedItems().Any(i => !i.IsFolder);
    public bool HasSelectedItem => SelectedCount > 0 || SelectedItem is not null;
    public bool HasMultipleSelected => SelectedCount > 1;
    public bool CanPreviewSelected => SelectedCount <= 1 && SelectedItem is { IsFolder: false };
    public bool CanDownloadSelected => GetEffectiveSelectedItems().Any(i => i.CanDownload);
    public bool CanOpenSelectedInDrive => SelectedItem is not null && !string.IsNullOrWhiteSpace(SelectedItem.RemoteFileId);
    public bool CanTrashSelected => !IsTrashView && GetEffectiveSelectedItems().Any(i => i.CanMoveToTrash);
    public bool CanRestoreSelected => IsTrashView && GetEffectiveSelectedItems().Any(i => i.IsInTrash);
    public bool CanDeleteSelectedPermanently => IsTrashView && GetEffectiveSelectedItems().Any(i => i.IsInTrash);
    public string SelectionCountDisplay => SelectedCount > 0 ? Loc.Format("FileHubSelectedCountFmt", SelectedCount) : string.Empty;
    public bool CanEmptyTrash => IsTrashView && IsAccountConnected && Files.Count > 0 && !IsLoading;
    public string EmptyStateText => IsTrashView ? Loc.Text("TrashEmptyHint") : Loc.Text("FileHubEmptyFolder");
    public bool IsImageOrPdfPreview => CurrentPreviewKind is PreviewKind.Image or PreviewKind.Pdf;
    public bool IsVideoPreview => CurrentPreviewKind == PreviewKind.Video;
    public bool IsAudioPreview => CurrentPreviewKind == PreviewKind.Audio;
    public bool IsPdfPreview => CurrentPreviewKind == PreviewKind.Pdf;
    public bool IsTextPreview => CurrentPreviewKind == PreviewKind.Text;
    public bool HasVisualBitmap => PreviewBitmap is not null && CurrentPreviewKind is PreviewKind.Image or PreviewKind.Video or PreviewKind.Audio or PreviewKind.Pdf;
    public string PdfPageDisplay => TotalPdfPages > 1
        ? Loc.Format("PreviewPageOfFmt", CurrentPdfPage, TotalPdfPages)
        : Loc.Format("PreviewPageFmt", CurrentPdfPage);
    public string AudioToggleLabel => IsAudioPlaying ? Loc.Text("PreviewStopAudio") : Loc.Text("PreviewPlayAudio");

    public ObservableCollection<FileHubBreadcrumbItem> Breadcrumbs { get; } = [];
    public ObservableCollection<FileHubItemViewModel> Files { get; } = [];
    public ObservableCollection<FileHubItemViewModel> SelectedItems { get; } = [];
    public ObservableCollection<string> SourceFilterOptions { get; } = [];

    public Func<Task<IReadOnlyList<string>>>? PickLocalFileForUploadCallback { get; set; }
    public Func<string, Task>? OpenLocalFileCallback { get; set; }
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Func<string, Task>? CopyTextCallback { get; set; }

    public FileHubViewModel(
        ISyncStateRepository repository,
        IGoogleDriveClient driveClient,
        IDriveSyncService syncService,
        FilePreviewService previewService,
        ILocalizationService localization)
    {
        _repository = repository;
        _driveClient = driveClient;
        _syncService = syncService;
        _previewService = previewService;
        Loc = localization;

        _driveFolderId = "root";
        _driveFolderName = Loc.Text("MyDriveLabel");
        _driveBreadcrumbs.Add(new FileHubBreadcrumbItem("root", _driveFolderName, NavigateToBreadcrumbAsync));
        _localBreadcrumbs.Add(new FileHubBreadcrumbItem(LocalRootCrumbId, Loc.Text("FileHubFilterLocal"), NavigateToBreadcrumbAsync));
        SyncBreadcrumbsFromActiveMode();
        RebuildFilterOptions();
        ApplyAccount(_syncService.CurrentAccount);

        Loc.LanguageChanged += (_, _) =>
        {
            if (_driveBreadcrumbs.Count > 0 && _driveBreadcrumbs[0].FolderId == "root")
            {
                _driveBreadcrumbs[0] = new FileHubBreadcrumbItem("root", Loc.Text("MyDriveLabel"), NavigateToBreadcrumbAsync);
                if (_driveFolderId == "root")
                {
                    _driveFolderName = Loc.Text("MyDriveLabel");
                }
            }

            if (_localBreadcrumbs.Count > 0 && _localBreadcrumbs[0].FolderId == LocalRootCrumbId)
            {
                _localBreadcrumbs[0] = new FileHubBreadcrumbItem(LocalRootCrumbId, Loc.Text("FileHubFilterLocal"), NavigateToBreadcrumbAsync);
            }

            SyncBreadcrumbsFromActiveMode();
            RebuildFilterOptions();
            ApplyAccount(_lastAccount);
            OnPropertyChanged(nameof(PdfPageDisplay));
            OnPropertyChanged(nameof(AudioToggleLabel));
            OnPropertyChanged(nameof(TrashTabDisplay));
            OnPropertyChanged(nameof(EmptyStateText));
            _ = RefreshAsync();
        };
    }

    public bool ShowBreadcrumbs => (SelectedSourceFilterIndex == 0 || SelectedSourceFilterIndex == 1) && string.IsNullOrWhiteSpace(SearchText);

    public void ApplyAccount(DriveAccount account)
    {
        _lastAccount = account;
        IsAccountConnected = account.IsConnected;

        if (!account.IsConnected)
        {
            QuotaShortText = Loc.Text("AccountDisconnected");
            QuotaBreakdownTooltip = string.Empty;
            TrashQuotaBadge = string.Empty;
            OnPropertyChanged(nameof(TrashTabDisplay));
            DriveBarWidth = 0;
            OtherBarWidth = 0;
            TrashBarWidth = 0;
            return;
        }

        var limitStr = account.Quota.LimitBytes > 0
            ? UiConverters.FormatBytes(account.Quota.LimitBytes)
            : Loc.Text("Unlimited");
        QuotaShortText = Loc.Format("StorageShortFmt", UiConverters.FormatBytes(account.Quota.UsageBytes), limitStr);
        TrashQuotaBadge = account.Quota.UsageInTrashBytes > 0
            ? UiConverters.FormatBytes(account.Quota.UsageInTrashBytes)
            : string.Empty;
        OnPropertyChanged(nameof(TrashTabDisplay));

        const double trackWidth = 170.0;
        if (account.Quota.LimitBytes > 0 || account.Quota.UsageBytes > 0)
        {
            var otherBytes = Math.Max(0, account.Quota.UsageBytes - account.Quota.UsageInDriveBytes - account.Quota.UsageInTrashBytes);
            QuotaBreakdownTooltip = Loc.Format(
                "QuotaBreakdownFmt",
                UiConverters.FormatBytes(account.Quota.UsageInDriveBytes),
                UiConverters.FormatBytes(otherBytes),
                UiConverters.FormatBytes(account.Quota.UsageInTrashBytes));
            (DriveBarWidth, OtherBarWidth, TrashBarWidth) = ComputeStorageBarWidths(account.Quota, trackWidth);
        }
        else
        {
            QuotaBreakdownTooltip = string.Empty;
            DriveBarWidth = 0;
            OtherBarWidth = 0;
            TrashBarWidth = 0;
        }
    }

    partial void OnCurrentPreviewKindChanged(PreviewKind value)
    {
        OnPropertyChanged(nameof(IsImageOrPdfPreview));
        OnPropertyChanged(nameof(IsVideoPreview));
        OnPropertyChanged(nameof(IsAudioPreview));
        OnPropertyChanged(nameof(IsPdfPreview));
        OnPropertyChanged(nameof(IsTextPreview));
        OnPropertyChanged(nameof(HasVisualBitmap));
    }

    partial void OnPreviewBitmapChanged(Bitmap? value) => OnPropertyChanged(nameof(HasVisualBitmap));
    partial void OnCurrentPdfPageChanged(int value) => OnPropertyChanged(nameof(PdfPageDisplay));
    partial void OnIsAudioPlayingChanged(bool value) => OnPropertyChanged(nameof(AudioToggleLabel));

    partial void OnSelectedItemChanged(FileHubItemViewModel? value)
    {
        if (SelectedItems.Count == 0 && value is not null)
        {
            value.IsSelected = true;
            SelectedItems.Add(value);
            SelectedCount = 1;
        }

        NotifySelectionStateChanged();

        if (value is { IsFolder: false } && SelectedCount <= 1)
        {
            _ = LoadPreviewAsync(value, 1);
        }
    }

    public void UpdateSelectedItems(IEnumerable<FileHubItemViewModel> items)
    {
        var next = items.Distinct().ToList();
        var nextSet = next.ToHashSet();

        foreach (var file in Files)
        {
            file.IsSelected = nextSet.Contains(file);
        }

        SelectedItems.Clear();
        foreach (var item in next)
        {
            SelectedItems.Add(item);
        }

        SelectedCount = SelectedItems.Count;
        NotifySelectionStateChanged();
    }

    private List<FileHubItemViewModel> GetEffectiveSelectedItems()
    {
        if (SelectedItems.Count > 0)
        {
            return SelectedItems.ToList();
        }

        return SelectedItem is not null ? [SelectedItem] : [];
    }

    private void NotifySelectionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedFile));
        OnPropertyChanged(nameof(HasSelectedItem));
        OnPropertyChanged(nameof(HasMultipleSelected));
        OnPropertyChanged(nameof(CanPreviewSelected));
        OnPropertyChanged(nameof(CanDownloadSelected));
        OnPropertyChanged(nameof(CanOpenSelectedInDrive));
        OnPropertyChanged(nameof(CanTrashSelected));
        OnPropertyChanged(nameof(CanRestoreSelected));
        OnPropertyChanged(nameof(CanDeleteSelectedPermanently));
        OnPropertyChanged(nameof(SelectionCountDisplay));
    }

    partial void OnSelectedSourceFilterIndexChanged(int value)
    {
        IsEmptyTrashConfirmVisible = false;
        if (value == 2)
        {
            ClosePreview();
        }

        SyncBreadcrumbsFromActiveMode();
        OnPropertyChanged(nameof(IsDriveFilter));
        OnPropertyChanged(nameof(IsLocalFilter));
        OnPropertyChanged(nameof(IsTrashView));
        OnPropertyChanged(nameof(TrashTabDisplay));
        OnPropertyChanged(nameof(CanEmptyTrash));
        OnPropertyChanged(nameof(CanTrashSelected));
        OnPropertyChanged(nameof(CanRestoreSelected));
        OnPropertyChanged(nameof(CanDeleteSelectedPermanently));
        OnPropertyChanged(nameof(EmptyStateText));
        if (value >= 0)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(ShowBreadcrumbs));

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanEmptyTrash));

    [RelayCommand]
    private async Task BuyMoreStorageAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(GoogleOneStorageUrl));
        }
    }

    [RelayCommand]
    private void SetListLayout()
    {
        IsGridLayout = false;
        ClearGridThumbnails();
    }

    [RelayCommand]
    private void SetGridLayout()
    {
        IsGridLayout = true;
        _ = LoadGridThumbnailsAsync();
    }

    [RelayCommand]
    private async Task PreviewSelectedAsync()
    {
        if (SelectedItem is { IsFolder: false })
        {
            await LoadPreviewAsync(SelectedItem, 1);
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        var targets = GetEffectiveSelectedItems()
            .Where(i => i.CanDownload && !string.IsNullOrWhiteSpace(i.RemoteFileId))
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        if (targets.Count == 1)
        {
            var single = targets[0];
            await DownloadFileAsync(single.RemoteFileId, single.Name, single.SizeBytes, single.IsGoogleWorkspaceDoc, single.MimeType);
            return;
        }

        if (!_driveClient.IsConnected)
        {
            StatusSummary = Loc.Text("ConnectBeforeSync");
            return;
        }

        TransferErrorBannerText = string.Empty;
        var downloadedCount = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var item = targets[i];
            StatusSummary = $"({i + 1}/{targets.Count}) {Loc.Format("DownloadingFileFmt", item.Name)}";
            try
            {
                await _syncService.DownloadRemoteFileAsync(
                    item.RemoteFileId,
                    item.Name,
                    item.SizeBytes,
                    item.IsGoogleWorkspaceDoc,
                    item.MimeType);
                downloadedCount++;
            }
            catch (Exception ex)
            {
                var err = Loc.Format("DownloadErrorBannerFmt", item.Name, ex.Message);
                TransferErrorBannerText = err;
                StatusSummary = err;
            }
        }

        if (downloadedCount > 0)
        {
            StatusSummary = Loc.Format("FilesDownloadedBatchFmt", downloadedCount);
        }
    }

    [RelayCommand]
    private async Task OpenSelectedInDriveAsync()
    {
        if (SelectedItem is null || string.IsNullOrWhiteSpace(SelectedItem.RemoteFileId))
        {
            return;
        }

        await OpenRemoteInDriveBrowserAsync(SelectedItem.RemoteFileId, SelectedItem.IsFolder);
    }

    [RelayCommand]
    private async Task TrashSelectedAsync()
    {
        var targets = GetEffectiveSelectedItems()
            .Where(i => i.CanMoveToTrash && !string.IsNullOrWhiteSpace(i.RemoteFileId))
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        if (targets.Count == 1)
        {
            await MoveToTrashAsync(targets[0].RemoteFileId, targets[0].Name);
            return;
        }

        if (!_driveClient.IsConnected)
        {
            StatusSummary = Loc.Text("ConnectBeforeSync");
            return;
        }

        IsLoading = true;
        var trashedCount = 0;
        try
        {
            foreach (var item in targets)
            {
                await _syncService.TrashRemoteItemAsync(item.RemoteFileId, item.Name);
                if (_previewedItem?.RemoteFileId == item.RemoteFileId)
                {
                    ClosePreview();
                }
                trashedCount++;
            }

            await Task.Delay(750);
            await _syncService.RefreshAccountInfoAsync();
            await RefreshAsync();
            StatusSummary = Loc.Format("FilesTrashedBatchFmt", trashedCount);
        }
        catch (Exception ex)
        {
            StatusSummary = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        var targets = GetEffectiveSelectedItems()
            .Where(i => i.IsInTrash && !string.IsNullOrWhiteSpace(i.RemoteFileId))
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        if (targets.Count == 1)
        {
            await RestoreFromTrashAsync(targets[0].RemoteFileId, targets[0].Name);
            return;
        }

        if (!_driveClient.IsConnected)
        {
            StatusSummary = Loc.Text("ConnectBeforeSync");
            return;
        }

        IsLoading = true;
        var restoredCount = 0;
        try
        {
            foreach (var item in targets)
            {
                await _syncService.RestoreRemoteItemAsync(item.RemoteFileId, item.Name);
                restoredCount++;
            }

            await Task.Delay(750);
            await _syncService.RefreshAccountInfoAsync();
            await RefreshAsync();
            StatusSummary = Loc.Format("FilesRestoredBatchFmt", restoredCount);
        }
        catch (Exception ex)
        {
            StatusSummary = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedPermanentlyAsync()
    {
        var targets = GetEffectiveSelectedItems()
            .Where(i => i.IsInTrash && !string.IsNullOrWhiteSpace(i.RemoteFileId))
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        if (targets.Count == 1)
        {
            await DeletePermanentlyAsync(targets[0].RemoteFileId, targets[0].Name);
            return;
        }

        if (!_driveClient.IsConnected)
        {
            StatusSummary = Loc.Text("ConnectBeforeSync");
            return;
        }

        IsLoading = true;
        var deletedCount = 0;
        try
        {
            foreach (var item in targets)
            {
                await _syncService.DeleteRemoteItemPermanentlyAsync(item.RemoteFileId, item.Name);
                deletedCount++;
            }

            await Task.Delay(750);
            await _syncService.RefreshAccountInfoAsync();
            await RefreshAsync();
            StatusSummary = Loc.Format("FilesDeletedPermanentBatchFmt", deletedCount);
        }
        catch (Exception ex)
        {
            StatusSummary = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectDriveFilter() => SelectedSourceFilterIndex = 0;

    [RelayCommand]
    private void SelectLocalFilter() => SelectedSourceFilterIndex = 1;

    [RelayCommand]
    private void SelectTrashFilter() => SelectedSourceFilterIndex = 2;

    [RelayCommand]
    private void ToggleTrashView()
    {
        SelectedSourceFilterIndex = IsTrashView ? 0 : 2;
    }

    [RelayCommand]
    private void RequestEmptyTrash()
    {
        if (CanEmptyTrash)
        {
            IsEmptyTrashConfirmVisible = true;
        }
    }

    [RelayCommand]
    private void CancelEmptyTrash() => IsEmptyTrashConfirmVisible = false;

    [RelayCommand]
    private async Task ConfirmEmptyTrashAsync()
    {
        if (!_driveClient.IsConnected)
        {
            StatusSummary = Loc.Text("ConnectBeforeSync");
            IsEmptyTrashConfirmVisible = false;
            return;
        }

        IsEmptyTrashConfirmVisible = false;
        IsLoading = true;
        try
        {
            await _syncService.EmptyRemoteTrashAsync();
            await Task.Delay(750);
            await _syncService.RefreshAccountInfoAsync();
            await RefreshAsync();
            StatusSummary = Loc.Text("TrashEmptiedFmt");
        }
        catch (Exception ex)
        {
            StatusSummary = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenPreviewedItemAsync()
    {
        if (!string.IsNullOrWhiteSpace(_previewLocalFilePath) &&
            File.Exists(_previewLocalFilePath) &&
            OpenLocalFileCallback is not null)
        {
            await OpenLocalFileCallback.Invoke(_previewLocalFilePath);
            return;
        }

        if (_previewedItem?.OpenActionCommand is not null)
        {
            await _previewedItem.OpenActionCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task DownloadPreviewedItemAsync()
    {
        if (_previewedItem?.DownloadCommand is not null)
        {
            await _previewedItem.DownloadCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    public void ClosePreview()
    {
        _previewCts?.Cancel();
        _previewService.StopAudio();
        IsAudioPlaying = false;
        IsPreviewVisible = false;
        _previewedItem = null;
        PreviewTextContent = null;
        ReplacePreviewBitmap(null);
    }

    private void ReplacePreviewBitmap(Bitmap? nextBitmap)
    {
        var previous = PreviewBitmap;
        PreviewBitmap = nextBitmap;
        if (previous is not null && !ReferenceEquals(previous, nextBitmap))
        {
            previous.Dispose();
        }
    }

    public void ClearGridThumbnails()
    {
        _thumbnailCts?.Cancel();
        foreach (var item in Files)
        {
            var old = item.ThumbnailBitmap;
            if (old is not null)
            {
                item.ThumbnailBitmap = null;
                old.Dispose();
            }
        }
    }

    public void ReleaseHeavyMediaResources()
    {
        ClearGridThumbnails();
        if (IsPreviewVisible)
        {
            ClosePreview();
        }
    }

    private void DisposeFileItems()
    {
        _thumbnailCts?.Cancel();
        if (Files.Count == 0)
        {
            return;
        }

        var oldItems = Files.ToList();
        Files.Clear();
        foreach (var old in oldItems)
        {
            old.Dispose();
        }
    }

    public void Dispose()
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;

        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;

        ClosePreview();
        DisposeFileItems();
    }

    [RelayCommand]
    private void ToggleAudioPlayback()
    {
        if (IsAudioPlaying)
        {
            _previewService.StopAudio();
            IsAudioPlaying = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(_previewLocalFilePath))
        {
            return;
        }

        IsAudioPlaying = true;
        _previewService.PlayAudio(_previewLocalFilePath, () =>
        {
            Dispatcher.UIThread.Post(() => IsAudioPlaying = false);
        });
    }

    [RelayCommand]
    private void PlayVideo()
    {
        if (!string.IsNullOrWhiteSpace(_previewLocalFilePath))
        {
            FilePreviewService.PlayVideoExternal(_previewLocalFilePath, PreviewFileName);
        }
    }

    [RelayCommand]
    private async Task PreviousPdfPageAsync()
    {
        if (_previewedItem is null || CurrentPdfPage <= 1)
        {
            return;
        }

        await LoadPreviewAsync(_previewedItem, CurrentPdfPage - 1);
    }

    [RelayCommand]
    private async Task NextPdfPageAsync()
    {
        if (_previewedItem is null || CurrentPdfPage >= TotalPdfPages)
        {
            return;
        }

        await LoadPreviewAsync(_previewedItem, CurrentPdfPage + 1);
    }

    [RelayCommand]
    private async Task NavigateToRootAsync()
    {
        SearchText = string.Empty;
        if (SelectedSourceFilterIndex == 1)
        {
            ResetLocalNavigation();
        }
        else
        {
            SelectedSourceFilterIndex = 0;
            ResetDriveNavigation();
        }

        SyncBreadcrumbsFromActiveMode();
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task NavigateBackAsync()
    {
        if (!CanNavigateBack)
        {
            return;
        }

        if (SelectedSourceFilterIndex == 1)
        {
            if (_localBreadcrumbs.Count <= 1)
            {
                return;
            }

            _localBreadcrumbs.RemoveAt(_localBreadcrumbs.Count - 1);
            ApplyLocalBreadcrumbState(_localBreadcrumbs[^1]);
        }
        else
        {
            if (_driveBreadcrumbs.Count <= 1)
            {
                return;
            }

            _driveBreadcrumbs.RemoveAt(_driveBreadcrumbs.Count - 1);
            var previous = _driveBreadcrumbs[^1];
            _driveFolderId = previous.FolderId;
            _driveFolderName = previous.Name;
        }

        SyncBreadcrumbsFromActiveMode();
        await RefreshAsync();
    }

    private async Task NavigateToBreadcrumbAsync(FileHubBreadcrumbItem item)
    {
        if (SelectedSourceFilterIndex == 1)
        {
            var index = _localBreadcrumbs.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            while (_localBreadcrumbs.Count - 1 > index)
            {
                _localBreadcrumbs.RemoveAt(_localBreadcrumbs.Count - 1);
            }

            ApplyLocalBreadcrumbState(item);
        }
        else
        {
            var index = _driveBreadcrumbs.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            while (_driveBreadcrumbs.Count - 1 > index)
            {
                _driveBreadcrumbs.RemoveAt(_driveBreadcrumbs.Count - 1);
            }

            _driveFolderId = item.FolderId;
            _driveFolderName = item.Name;
        }

        SyncBreadcrumbsFromActiveMode();
        await RefreshAsync();
    }

    private void ResetDriveNavigation()
    {
        _driveFolderId = "root";
        _driveFolderName = Loc.Text("MyDriveLabel");
        _driveBreadcrumbs.Clear();
        _driveBreadcrumbs.Add(new FileHubBreadcrumbItem("root", _driveFolderName, NavigateToBreadcrumbAsync));
    }

    private void ResetLocalNavigation()
    {
        _localSyncFolderId = null;
        _localSubPath = string.Empty;
        _localBreadcrumbs.Clear();
        _localBreadcrumbs.Add(new FileHubBreadcrumbItem(LocalRootCrumbId, Loc.Text("FileHubFilterLocal"), NavigateToBreadcrumbAsync));
    }

    private void ApplyLocalBreadcrumbState(FileHubBreadcrumbItem item)
    {
        if (string.Equals(item.FolderId, LocalRootCrumbId, StringComparison.Ordinal) ||
            !item.FolderId.StartsWith(LocalFolderCrumbPrefix, StringComparison.Ordinal))
        {
            _localSyncFolderId = null;
            _localSubPath = string.Empty;
            return;
        }

        var payload = item.FolderId[LocalFolderCrumbPrefix.Length..];
        var sep = payload.IndexOf('|');
        if (sep < 0)
        {
            _localSyncFolderId = payload;
            _localSubPath = string.Empty;
        }
        else
        {
            _localSyncFolderId = payload[..sep];
            _localSubPath = payload[(sep + 1)..];
        }
    }

    private void SyncBreadcrumbsFromActiveMode()
    {
        var active = SelectedSourceFilterIndex == 1 ? _localBreadcrumbs : _driveBreadcrumbs;
        Breadcrumbs.Clear();
        foreach (var crumb in active)
        {
            Breadcrumbs.Add(crumb);
        }

        if (SelectedSourceFilterIndex == 1)
        {
            CurrentFolderId = _localSyncFolderId ?? LocalRootCrumbId;
            CurrentFolderName = _localBreadcrumbs.LastOrDefault()?.Name ?? Loc.Text("FileHubFilterLocal");
        }
        else
        {
            CurrentFolderId = _driveFolderId;
            CurrentFolderName = _driveFolderName;
        }

        OnPropertyChanged(nameof(RootBreadcrumbLabel));
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(ShowBreadcrumbs));
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            ApplyPage(CurrentPage - 1);
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            ApplyPage(CurrentPage + 1);
        }
    }

    private void ApplyPage(int pageNumber)
    {
        var totalItems = _allPageFactories.Count;
        TotalPages = Math.Max(1, (totalItems + ItemsPerPage - 1) / ItemsPerPage);
        CurrentPage = Math.Clamp(pageNumber, 1, TotalPages);

        DisposeFileItems();
        SelectedItems.Clear();
        SelectedCount = 0;
        SelectedItem = null;
        NotifySelectionStateChanged();
        if (totalItems > 0)
        {
            var skip = (CurrentPage - 1) * ItemsPerPage;
            foreach (var factory in _allPageFactories.Skip(skip).Take(ItemsPerPage))
            {
                Files.Add(factory());
            }
        }

        IsFolderEmpty = totalItems == 0;
        OnPropertyChanged(nameof(CanEmptyTrash));
        OnPropertyChanged(nameof(EmptyStateText));

        if (IsGridLayout && Files.Count > 0)
        {
            _ = LoadGridThumbnailsAsync();
        }
    }

    public void ClearWhenDisconnected()
    {
        _refreshCts?.Cancel();
        _thumbnailCts?.Cancel();
        ClosePreview();
        _allPageFactories = [];
        TotalPages = 1;
        CurrentPage = 1;
        DisposeFileItems();
        SelectedItems.Clear();
        SelectedCount = 0;
        SelectedItem = null;
        SearchText = string.Empty;
        ResetDriveNavigation();
        ResetLocalNavigation();
        SyncBreadcrumbsFromActiveMode();
        IsEmptyTrashConfirmVisible = false;
        IsFolderEmpty = true;
        IsLoading = false;
        OnPropertyChanged(nameof(CanEmptyTrash));
        OnPropertyChanged(nameof(EmptyStateText));
        StatusSummary = Loc.Text("ConnectBeforeSync");
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await RefreshInternalAsync();
    }

    private async Task<bool> RefreshInternalAsync()
    {
        var filter = SelectedSourceFilterIndex;
        var hasNetwork = _driveClient.IsConnected && _syncService.IsOnline;
        if (!hasNetwork && filter != 1)
        {
            ClearWhenDisconnected();
            StatusSummary = !_driveClient.IsConnected
                ? Loc.Text("ConnectBeforeSync")
                : Loc.Text("OfflineModeNotice");
            return false;
        }

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        var refreshCts = new CancellationTokenSource();
        _refreshCts = refreshCts;
        var ct = refreshCts.Token;

        OnPropertyChanged(nameof(ShowBreadcrumbs));
        IsLoading = true;
        StatusSummary = Loc.Text("FileHubLoading");

        try
        {
            var query = SearchText.Trim();
            var culture = CultureInfo.GetCultureInfo(Loc.CurrentLanguage);

            var folderSource = hasNetwork
                ? await _syncService.GetSyncFoldersAsync(ct)
                : await _repository.GetSyncFoldersAsync(ct);
            var syncFolders = folderSource.ToDictionary(f => f.Id, f => f, StringComparer.Ordinal);
            var activeFolderIds = syncFolders.Keys.ToHashSet(StringComparer.Ordinal);

            var factories = new List<Func<FileHubItemViewModel>>();

            if (filter == 2)
            {
                if (hasNetwork)
                {
                    var trashedNodes = await _driveClient.ListTrashedFilesAsync(200, ct);
                    foreach (var node in trashedNodes)
                    {
                        if (!string.IsNullOrWhiteSpace(query) &&
                            !node.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var captured = node;
                        factories.Add(() => CreateTrashItem(captured, culture));
                    }
                }
            }
            else if (filter == 1)
            {
                if (!string.IsNullOrWhiteSpace(query))
                {
                    var rawLocal = await _repository.SearchTrackedFilesAsync(query, null, 2000, ct);
                    var filteredEntries = await Task.Run(() =>
                    {
                        var list = new List<(TrackedFileEntry Entry, bool ExistsLocally)>();
                        var checkDisk = !hasNetwork;
                        foreach (var entry in rawLocal)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }

                            if (!activeFolderIds.Contains(entry.SyncFolderId) ||
                                !syncFolders.TryGetValue(entry.SyncFolderId, out var localFolder))
                            {
                                continue;
                            }

                            var diskPath = Path.Combine(
                                localFolder.LocalPath,
                                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                            var exists = entry.IsFolder ? Directory.Exists(diskPath) : File.Exists(diskPath);
                            if (checkDisk && !exists)
                            {
                                continue;
                            }

                            list.Add((entry, exists));
                        }

                        return list;
                    }, ct);

                    foreach (var pair in filteredEntries)
                    {
                        var capturedEntry = pair.Entry;
                        var capturedExists = pair.ExistsLocally;
                        factories.Add(() => CreateTrackedItem(capturedEntry, syncFolders, culture, capturedExists));
                    }
                }
                else
                {
                    await PopulateLocalHierarchyFactoriesAsync(factories, syncFolders, hasNetwork, culture, ct);
                }
            }
            else
            {
                var allTracked = activeFolderIds.Count > 0
                    ? (await _repository.SearchTrackedFilesAsync(string.Empty, null, 2000, ct))
                        .Where(t => activeFolderIds.Contains(t.SyncFolderId))
                        .ToList()
                    : [];
                var trackedByRemoteId = allTracked
                    .Where(t => !string.IsNullOrWhiteSpace(t.RemoteFileId))
                    .GroupBy(t => t.RemoteFileId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                var mappedByRemoteId = syncFolders.Values
                    .Where(f => !string.IsNullOrWhiteSpace(f.RemoteFolderId))
                    .GroupBy(f => f.RemoteFolderId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                IReadOnlyList<RemoteDriveNode> remoteNodes = string.IsNullOrWhiteSpace(query)
                    ? await _driveClient.ListChildrenAsync(_driveFolderId, foldersOnly: false, ct)
                    : await _driveClient.SearchRemoteFilesAsync(query, 100, ct);

                foreach (var node in remoteNodes)
                {
                    if (node.Trashed)
                    {
                        continue;
                    }

                    var captured = node;
                    if (node.IsFolder)
                    {
                        mappedByRemoteId.TryGetValue(captured.Id, out var mappedFolder);
                        factories.Add(() => CreateRemoteFolderItem(captured, culture, mappedFolder));
                    }
                    else
                    {
                        factories.Add(() => CreateRemoteFileItem(captured, trackedByRemoteId, syncFolders, culture));
                    }
                }
            }

            if (ct.IsCancellationRequested)
            {
                return false;
            }

            _allPageFactories = factories;
            ApplyPage(1);

            StatusSummary = filter == 1 && !string.IsNullOrWhiteSpace(query)
                ? (!hasNetwork
                    ? $"{Loc.Format("FileHubSearchCountFmt", factories.Count)} • {Loc.Text("OfflineModeNotice")}"
                    : Loc.Format("FileHubSearchCountFmt", factories.Count))
                : filter == 1
                    ? (!hasNetwork
                        ? $"{Loc.Format("FileHubCountFmt", factories.Count, CurrentFolderName)} • {Loc.Text("OfflineModeNotice")}"
                        : Loc.Format("FileHubCountFmt", factories.Count, CurrentFolderName))
                : filter == 2
                    ? Loc.Format("FileHubCountFmt", factories.Count, Loc.Text("FileHubFilterTrash"))
                    : string.IsNullOrWhiteSpace(query) && filter == 0
                        ? Loc.Format("FileHubCountFmt", factories.Count, CurrentFolderName)
                        : Loc.Format("FileHubSearchCountFmt", factories.Count);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _syncService.ReportNetworkFailure();
            StatusSummary = Loc.Format("FileHubErrorFmt", ex.Message);
            return false;
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, refreshCts))
            {
                IsLoading = false;
            }
        }
    }

    private FileHubItemViewModel CreateTrashItem(RemoteDriveNode node, CultureInfo culture)
    {
        var (iconData, iconBrush, isVideo) = node.IsFolder
            ? (FolderIconSvg, BrushMutedGray, false)
            : ResolveFileVisualStyle(node.Name, node.MimeType);

        var nodeId = node.Id;
        var nodeName = node.Name;
        var actionLabel = node.IsFolder ? string.Empty : Loc.Text("PreviewButton");
        FileHubItemViewModel? item = null;
        var previewCommand = !node.IsFolder
            ? new AsyncRelayCommand(() => LoadPreviewAsync(item!, 1))
            : null;

        item = new FileHubItemViewModel
        {
            Name = node.Name,
            PathDisplay = Loc.Text("FileHubFilterTrash"),
            OriginDisplay = Loc.Text("FileHubFilterTrash"),
            OriginBrush = BrushTrashSlate,
            SizeDisplay = node.IsFolder || node.IsGoogleWorkspaceDoc ? "—" : UiConverters.FormatBytes(node.SizeBytes),
            SizeBytes = node.SizeBytes,
            ModifiedDisplay = node.ModifiedTimeUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", culture),
            MimeType = node.MimeType,
            IsFolder = node.IsFolder,
            IsVideoFile = isVideo,
            IsGoogleWorkspaceDoc = node.IsGoogleWorkspaceDoc,
            HasLocalFile = false,
            IsInTrash = true,
            RemoteFileId = nodeId,
            ActionLabel = actionLabel,
            DownloadLabel = Loc.Text("DownloadButton"),
            PreviewLabel = Loc.Text("PreviewButton"),
            FileIconData = iconData,
            FileIconBrush = iconBrush,
            RestoreLabel = Loc.Text("RestoreFromTrashButton"),
            DeletePermanentlyLabel = Loc.Text("DeletePermanentlyButton"),
            CopyNameLabel = Loc.Text("CopyNameMenu"),
            OpenActionCommand = previewCommand,
            PreviewCommand = previewCommand,
            DownloadCommand = !node.IsFolder
                ? new AsyncRelayCommand(() => DownloadFileAsync(nodeId, nodeName, node.SizeBytes, node.IsGoogleWorkspaceDoc, node.MimeType))
                : null,
            RestoreCommand = new AsyncRelayCommand(() => RestoreFromTrashAsync(nodeId, nodeName)),
            DeletePermanentlyCommand = new AsyncRelayCommand(() => DeletePermanentlyAsync(nodeId, nodeName)),
            CopyNameCommand = new AsyncRelayCommand(() => CopyToClipboardAsync(nodeName))
        };
        return item;
    }

    private FileHubItemViewModel CreateRemoteFolderItem(
        RemoteDriveNode node,
        CultureInfo culture,
        LocalSyncFolder? mappedFolder = null)
    {
        var folderId = node.Id;
        var folderName = node.Name;
        var displayName = !string.IsNullOrWhiteSpace(mappedFolder?.CustomAlias)
            ? mappedFolder!.CustomAlias!.Trim()
            : node.Name;
        var iconData = FolderCustomizationPresets.ResolveIconSvg(mappedFolder?.CustomIconKey);
        var normalizedHex = FolderCustomizationPresets.NormalizeColorHex(mappedFolder?.CustomColorHex);
        var iconBrush = !string.IsNullOrWhiteSpace(normalizedHex)
            ? SolidColorBrush.Parse(normalizedHex)
            : BrushMutedGray;

        return new FileHubItemViewModel
        {
            Name = displayName,
            PathDisplay = Loc.Text("FileHubStatusFolder"),
            OriginDisplay = Loc.Text("FileHubStatusFolder"),
            OriginBrush = BrushFolderBlue,
            SizeDisplay = "—",
            ModifiedDisplay = node.ModifiedTimeUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", culture),
            MimeType = node.MimeType,
            IsFolder = true,
            HasLocalFile = false,
            RemoteFileId = folderId,
            FileIconData = iconData,
            FileIconBrush = iconBrush,
            ActionLabel = Loc.Text("FileHubOpenFolder"),
            OpenInDriveLabel = Loc.Text("OpenInDriveButton"),
            TrashLabel = Loc.Text("MoveToTrashButton"),
            CopyNameLabel = Loc.Text("CopyNameMenu"),
            CopyLinkLabel = Loc.Text("CopyDriveLinkMenu"),
            OpenActionCommand = new AsyncRelayCommand(() => OpenRemoteFolderAsync(folderId, displayName)),
            OpenInDriveCommand = new AsyncRelayCommand(() => OpenRemoteInDriveBrowserAsync(folderId, true)),
            TrashCommand = new AsyncRelayCommand(() => MoveToTrashAsync(folderId, folderName)),
            CopyNameCommand = new AsyncRelayCommand(() => CopyToClipboardAsync(folderName)),
            CopyDriveLinkCommand = new AsyncRelayCommand(() => CopyToClipboardAsync($"https://drive.google.com/drive/folders/{folderId}"))
        };
    }

    private FileHubItemViewModel CreateRemoteFileItem(
        RemoteDriveNode node,
        IReadOnlyDictionary<string, TrackedFileEntry> trackedByRemoteId,
        IReadOnlyDictionary<string, LocalSyncFolder> syncFolders,
        CultureInfo culture)
    {
        string? resolvedLocalPath = null;
        var relativeDisplay = CurrentFolderName;
        var isSynced = false;

        if (trackedByRemoteId.TryGetValue(node.Id, out var tracked) &&
            syncFolders.TryGetValue(tracked.SyncFolderId, out var syncFolder))
        {
            var candidate = Path.Combine(
                syncFolder.LocalPath,
                tracked.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                resolvedLocalPath = candidate;
                relativeDisplay = tracked.RelativePath;
                isSynced = true;
            }
        }

        var nodeId = node.Id;
        var nodeName = node.Name;
        var (iconData, iconBrush, isVideo) = ResolveFileVisualStyle(node.Name, node.MimeType);
        var item = new FileHubItemViewModel
        {
            Name = node.Name,
            PathDisplay = relativeDisplay,
            OriginDisplay = isSynced ? Loc.Text("FileHubStatusSynced") : Loc.Text("FileHubStatusRemoteOnly"),
            OriginBrush = isSynced ? BrushSyncedGreen : BrushMutedGray,
            SizeDisplay = node.IsGoogleWorkspaceDoc ? "—" : UiConverters.FormatBytes(node.SizeBytes),
            SizeBytes = node.SizeBytes,
            ModifiedDisplay = node.ModifiedTimeUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", culture),
            MimeType = node.MimeType,
            IsFolder = false,
            IsVideoFile = isVideo,
            IsGoogleWorkspaceDoc = node.IsGoogleWorkspaceDoc,
            HasLocalFile = isSynced,
            LocalFullPath = resolvedLocalPath,
            RemoteFileId = nodeId,
            FileIconData = iconData,
            FileIconBrush = iconBrush,
            ActionLabel = isSynced ? Loc.Text("FileHubOpenLocal") : Loc.Text("FileHubOpenDrive"),
            DownloadLabel = Loc.Text("DownloadButton"),
            PreviewLabel = Loc.Text("PreviewButton"),
            OpenInDriveLabel = Loc.Text("OpenInDriveButton"),
            TrashLabel = Loc.Text("MoveToTrashButton"),
            CopyNameLabel = Loc.Text("CopyNameMenu"),
            CopyLinkLabel = Loc.Text("CopyDriveLinkMenu"),
            OpenActionCommand = new AsyncRelayCommand(() => OpenFileItemAsync(isSynced, resolvedLocalPath, nodeId)),
            DownloadCommand = new AsyncRelayCommand(() => DownloadFileAsync(nodeId, nodeName, node.SizeBytes, node.IsGoogleWorkspaceDoc, node.MimeType)),
            OpenInDriveCommand = new AsyncRelayCommand(() => OpenRemoteInDriveBrowserAsync(nodeId, false)),
            TrashCommand = new AsyncRelayCommand(() => MoveToTrashAsync(nodeId, nodeName)),
            CopyNameCommand = new AsyncRelayCommand(() => CopyToClipboardAsync(nodeName)),
            CopyDriveLinkCommand = new AsyncRelayCommand(() => CopyToClipboardAsync($"https://drive.google.com/file/d/{nodeId}/view"))
        };

        item.PreviewCommand = new AsyncRelayCommand(() => LoadPreviewAsync(item, 1));
        return item;
    }

    public Task ActivateItemAsync(FileHubItemViewModel? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        if (!item.IsFolder && !item.HasLocalFile && item.PreviewCommand is not null)
        {
            return item.PreviewCommand.ExecuteAsync(null);
        }

        return item.OpenActionCommand is not null
            ? item.OpenActionCommand.ExecuteAsync(null)
            : Task.CompletedTask;
    }

    [RelayCommand]
    private void DismissTransferErrorBanner() => TransferErrorBannerText = string.Empty;

    [RelayCommand]
    public void OpenCreateFolder()
    {
        if (IsTrashView)
        {
            IsCreateFolderOpen = false;
            return;
        }

        if (SelectedSourceFilterIndex == 1 && string.IsNullOrWhiteSpace(_localSyncFolderId))
        {
            StatusSummary = Loc.Text("NewFolderLocalRootHint");
            IsCreateFolderOpen = false;
            return;
        }

        NewFolderName = string.Empty;
        IsCreateFolderOpen = true;
    }

    [RelayCommand]
    private void ToggleCreateFolder()
    {
        if (IsCreateFolderOpen)
        {
            IsCreateFolderOpen = false;
            return;
        }

        OpenCreateFolder();
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        if (IsTrashView)
        {
            return;
        }

        var trimmed = (NewFolderName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Contains('/')
            || trimmed.Contains('\\')
            || trimmed is "." or "..")
        {
            StatusSummary = Loc.Text("NewFolderNameRequired");
            return;
        }

        if (SelectedSourceFilterIndex == 1)
        {
            if (string.IsNullOrWhiteSpace(_localSyncFolderId))
            {
                StatusSummary = Loc.Text("NewFolderLocalRootHint");
                return;
            }

            IsLoading = true;
            try
            {
                var syncFolder = await _repository.GetSyncFolderByIdAsync(_localSyncFolderId);
                if (syncFolder is null)
                {
                    StatusSummary = Loc.Text("NewFolderLocalRootHint");
                    return;
                }

                var fullRoot = Path.GetFullPath(syncFolder.LocalPath);
                var baseDir = string.IsNullOrWhiteSpace(_localSubPath)
                    ? fullRoot
                    : Path.GetFullPath(Path.Combine(fullRoot, _localSubPath.Replace('/', Path.DirectorySeparatorChar)));
                var targetDir = Path.GetFullPath(Path.Combine(baseDir, trimmed));
                var rootPrefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!targetDir.StartsWith(rootPrefix, StringComparison.Ordinal))
                {
                    StatusSummary = Loc.Text("NewFolderNameRequired");
                    return;
                }

                Directory.CreateDirectory(targetDir);
                IsCreateFolderOpen = false;
                NewFolderName = string.Empty;

                if (_driveClient.IsConnected && _syncService.IsOnline)
                {
                    _ = Task.Run(() => _syncService.TriggerFolderSyncAsync(syncFolder.Id));
                }

                await RefreshAsync();
                StatusSummary = Loc.Format("FolderCreatedFmt", trimmed);
            }
            catch (Exception ex)
            {
                StatusSummary = Loc.Format("FileHubErrorFmt", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
            return;
        }

        if (!_driveClient.IsConnected)
        {
            StatusSummary = Loc.Text("ConnectBeforeSync");
            return;
        }

        if (!_syncService.IsOnline)
        {
            StatusSummary = Loc.Text("OfflineModeNotice");
            return;
        }

        IsLoading = true;
        try
        {
            var created = await _driveClient.CreateFolderAsync(trimmed, _driveFolderId);
            IsCreateFolderOpen = false;
            NewFolderName = string.Empty;
            await RefreshAsync();
            StatusSummary = Loc.Format("FolderCreatedFmt", created.Name);
        }
        catch (Exception ex)
        {
            StatusSummary = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task UploadDroppedFilesAsync(IEnumerable<string> localPaths)
    {
        if (!_driveClient.IsConnected)
        {
            StatusSummary = Loc.Text("ConnectBeforeSync");
            return;
        }

        TransferErrorBannerText = string.Empty;
        var targetFolder = SelectedSourceFilterIndex == 0 ? CurrentFolderId : "root";
        var uploadedCount = 0;

        IsLoading = true;
        try
        {
            foreach (var path in localPaths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var fileName = Path.GetFileName(path);
                StatusSummary = Loc.Format("FileHubUploadingFmt", fileName);
                try
                {
                    await _syncService.UploadSingleFileAsync(path, targetFolder);
                    uploadedCount++;
                }
                catch (Exception ex)
                {
                    var err = Loc.Format("UploadErrorBannerFmt", fileName, ex.Message);
                    TransferErrorBannerText = err;
                    StatusSummary = err;
                }
            }

            if (uploadedCount > 0)
            {
                await RefreshAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UploadFileAsync()
    {
        if (!_driveClient.IsConnected)
        {
            StatusSummary = Loc.Text("ConnectBeforeSync");
            return;
        }

        if (PickLocalFileForUploadCallback is null)
        {
            return;
        }

        var localPaths = await PickLocalFileForUploadCallback.Invoke();
        if (localPaths is null || localPaths.Count == 0)
        {
            return;
        }

        await UploadDroppedFilesAsync(localPaths);
    }

    private async Task LoadPreviewAsync(FileHubItemViewModel item, int pdfPage)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        var previewCts = new CancellationTokenSource();
        _previewCts = previewCts;
        var ct = previewCts.Token;

        _previewService.StopAudio();
        IsAudioPlaying = false;
        _previewedItem = item;
        IsPreviewVisible = true;
        IsPreviewLoading = true;
        PreviewFileName = item.Name;
        PreviewMetaText = $"{item.OriginDisplay} • {item.SizeDisplay} • {item.ModifiedDisplay}";
        PreviewMessage = Loc.Text("PreviewLoading");
        PreviewTextContent = null;
        ReplacePreviewBitmap(null);
        CurrentPreviewKind = PreviewKind.None;
        CurrentPdfPage = pdfPage;

        try
        {
            var result = await _previewService.BuildPreviewAsync(
                item.Name,
                item.LocalFullPath,
                item.RemoteFileId,
                item.IsGoogleWorkspaceDoc,
                item.MimeType,
                pdfPage,
                ct);

            if (ct.IsCancellationRequested)
            {
                result.VisualBitmap?.Dispose();
                return;
            }

            _previewLocalFilePath = result.LocalFilePath;
            CurrentPreviewKind = result.Kind;
            ReplacePreviewBitmap(result.VisualBitmap);
            PreviewTextContent = result.TextContent;
            TotalPdfPages = result.TotalPages;
            CurrentPdfPage = result.PageNumber;

            if (!string.IsNullOrWhiteSpace(result.MediaInfo))
            {
                PreviewMetaText = $"{item.OriginDisplay} • {item.SizeDisplay} • {result.MediaInfo}";
            }

            PreviewMessage = result.Kind == PreviewKind.Unsupported
                ? Loc.Text("PreviewUnsupported")
                : string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CurrentPreviewKind = PreviewKind.Unsupported;
            PreviewMessage = Loc.Format("PreviewErrorFmt", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_previewCts, previewCts))
            {
                IsPreviewLoading = false;
            }
        }
    }

    private async Task LoadGridThumbnailsAsync()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = new CancellationTokenSource();
        var ct = _thumbnailCts.Token;

        var candidates = Files
            .Where(f => !f.IsFolder && !f.IsDisposed && f.ThumbnailBitmap is null)
            .Take(24)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(2, 2);
        var tasks = candidates.Select(async item =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (ct.IsCancellationRequested || item.IsDisposed)
                {
                    return;
                }

                var thumb = await _previewService.TryLoadGridThumbnailAsync(
                    item.Name,
                    item.LocalFullPath,
                    item.RemoteFileId,
                    item.SizeBytes,
                    ct);

                if (thumb is not null)
                {
                    if (ct.IsCancellationRequested || item.IsDisposed)
                    {
                        thumb.Dispose();
                        return;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ct.IsCancellationRequested || item.IsDisposed)
                        {
                            thumb.Dispose();
                            return;
                        }

                        var old = item.ThumbnailBitmap;
                        item.ThumbnailBitmap = thumb;
                        if (old is not null && !ReferenceEquals(old, thumb))
                        {
                            old.Dispose();
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                gate.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static (string IconData, IBrush Brush, bool IsVideo) ResolveFileVisualStyle(string fileName, string? mimeType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        if (ext is ".xlsx" or ".xls" or ".xlsm" or ".ods" or ".csv" ||
            string.Equals(mimeType, "application/vnd.google-apps.spreadsheet", StringComparison.OrdinalIgnoreCase))
        {
            return (SheetIconSvg, BrushSheetGreen, false);
        }

        if (ext is ".pdf")
        {
            return (PdfIconSvg, BrushPdfRed, false);
        }

        if (ext is ".docx" or ".doc" or ".odt" or ".rtf" ||
            string.Equals(mimeType, "application/vnd.google-apps.document", StringComparison.OrdinalIgnoreCase))
        {
            return (DocIconSvg, BrushDocBlue, false);
        }

        if (ext is ".pptx" or ".ppt" or ".odp" ||
            string.Equals(mimeType, "application/vnd.google-apps.presentation", StringComparison.OrdinalIgnoreCase))
        {
            return (DocIconSvg, BrushSlideAmber, false);
        }

        if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".flv" or ".m4v" or ".wmv" or ".3gp")
        {
            return (VideoIconSvg, BrushVideoCyan, true);
        }

        if (ext is ".mp3" or ".wav" or ".ogg" or ".flac" or ".m4a" or ".aac" or ".opus" or ".wma")
        {
            return (AudioIconSvg, BrushAudioPurple, false);
        }

        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".svg" or ".avif" or ".heic")
        {
            return (ImageIconSvg, BrushImagePink, false);
        }

        return (DocIconSvg, BrushTrashSlate, false);
    }

    private FileHubItemViewModel CreateTrackedItem(
        TrackedFileEntry entry,
        IReadOnlyDictionary<string, LocalSyncFolder> syncFolders,
        CultureInfo culture,
        bool? knownExistsLocally = null)
    {
        string? fullLocalPath = null;
        if (syncFolders.TryGetValue(entry.SyncFolderId, out var folder))
        {
            fullLocalPath = Path.Combine(
                folder.LocalPath,
                entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        var existsLocally = knownExistsLocally ?? (!string.IsNullOrWhiteSpace(fullLocalPath) && File.Exists(fullLocalPath));
        var remoteId = entry.RemoteFileId;
        var fileName = Path.GetFileName(entry.RelativePath);
        var (iconData, iconBrush, isVideo) = entry.IsFolder
            ? (FolderIconSvg, BrushMutedGray, false)
            : ResolveFileVisualStyle(fileName, null);

        var isConnected = _driveClient.IsConnected;
        var item = new FileHubItemViewModel
        {
            Name = fileName,
            PathDisplay = entry.RelativePath,
            OriginDisplay = isConnected ? Loc.Text("FileHubStatusSynced") : Loc.Text("FileHubStatusLocal"),
            OriginBrush = BrushSyncedGreen,
            SizeDisplay = UiConverters.FormatBytes(entry.SizeBytes),
            SizeBytes = entry.SizeBytes,
            ModifiedDisplay = entry.LocalModifiedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", culture),
            IsFolder = entry.IsFolder,
            IsVideoFile = isVideo,
            HasLocalFile = existsLocally,
            LocalFullPath = fullLocalPath,
            RemoteFileId = isConnected ? remoteId : string.Empty,
            FileIconData = iconData,
            FileIconBrush = iconBrush,
            ActionLabel = existsLocally ? Loc.Text("FileHubOpenLocal") : Loc.Text("FileHubOpenDrive"),
            DownloadLabel = Loc.Text("DownloadButton"),
            PreviewLabel = Loc.Text("PreviewButton"),
            OpenInDriveLabel = Loc.Text("OpenInDriveButton"),
            TrashLabel = Loc.Text("MoveToTrashButton"),
            CopyNameLabel = Loc.Text("CopyNameMenu"),
            CopyLinkLabel = Loc.Text("CopyDriveLinkMenu"),
            OpenActionCommand = new AsyncRelayCommand(() => OpenFileItemAsync(existsLocally, fullLocalPath, isConnected ? remoteId : string.Empty)),
            DownloadCommand = isConnected && !entry.IsFolder && !string.IsNullOrWhiteSpace(remoteId)
                ? new AsyncRelayCommand(() => DownloadFileAsync(remoteId, fileName, entry.SizeBytes, false, null))
                : null,
            OpenInDriveCommand = isConnected && !string.IsNullOrWhiteSpace(remoteId)
                ? new AsyncRelayCommand(() => OpenRemoteInDriveBrowserAsync(remoteId, entry.IsFolder))
                : null,
            TrashCommand = isConnected && !string.IsNullOrWhiteSpace(remoteId)
                ? new AsyncRelayCommand(() => MoveToTrashAsync(remoteId, fileName))
                : null,
            CopyNameCommand = new AsyncRelayCommand(() => CopyToClipboardAsync(fileName)),
            CopyDriveLinkCommand = isConnected && !string.IsNullOrWhiteSpace(remoteId)
                ? new AsyncRelayCommand(() => CopyToClipboardAsync(entry.IsFolder
                    ? $"https://drive.google.com/drive/folders/{remoteId}"
                    : $"https://drive.google.com/file/d/{remoteId}/view"))
                : null
        };

        if (!entry.IsFolder)
        {
            item.PreviewCommand = new AsyncRelayCommand(() => LoadPreviewAsync(item, 1));
        }

        return item;
    }

    private async Task MoveToTrashAsync(string remoteFileId, string fileName)
    {
        if (!_driveClient.IsConnected || string.IsNullOrWhiteSpace(remoteFileId))
        {
            return;
        }

        IsLoading = true;
        try
        {
            await _syncService.TrashRemoteItemAsync(remoteFileId, fileName);
            if (_previewedItem?.RemoteFileId == remoteFileId)
            {
                ClosePreview();
            }

            await Task.Delay(750);
            await _syncService.RefreshAccountInfoAsync();
            await RefreshAsync();
            StatusSummary = Loc.Format("ItemMovedToTrashFmt", fileName);
        }
        catch (Exception ex)
        {
            StatusSummary = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RestoreFromTrashAsync(string remoteFileId, string fileName)
    {
        if (!_driveClient.IsConnected || string.IsNullOrWhiteSpace(remoteFileId))
        {
            return;
        }

        IsLoading = true;
        try
        {
            await _syncService.RestoreRemoteItemAsync(remoteFileId, fileName);
            await Task.Delay(750);
            await _syncService.RefreshAccountInfoAsync();
            await RefreshAsync();
            StatusSummary = Loc.Format("ItemRestoredFmt", fileName);
        }
        catch (Exception ex)
        {
            StatusSummary = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DeletePermanentlyAsync(string remoteFileId, string fileName)
    {
        if (!_driveClient.IsConnected || string.IsNullOrWhiteSpace(remoteFileId))
        {
            return;
        }

        IsLoading = true;
        try
        {
            await _syncService.DeleteRemoteItemPermanentlyAsync(remoteFileId, fileName);
            await Task.Delay(750);
            await _syncService.RefreshAccountInfoAsync();
            await RefreshAsync();
            StatusSummary = Loc.Format("ItemDeletedPermanentlyFmt", fileName);
        }
        catch (Exception ex)
        {
            StatusSummary = Loc.Format("FileHubErrorFmt", ex.Message);
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
        StatusSummary = Loc.Text("CopiedFeedbackFmt");
    }

    private async Task OpenRemoteInDriveBrowserAsync(string remoteFileId, bool isFolder)
    {
        if (OpenExternalUriCallback is null || string.IsNullOrWhiteSpace(remoteFileId))
        {
            return;
        }

        var url = isFolder
            ? $"https://drive.google.com/drive/folders/{remoteFileId}"
            : $"https://drive.google.com/file/d/{remoteFileId}/view";
        await OpenExternalUriCallback.Invoke(new Uri(url));
    }

    private async Task DownloadFileAsync(
        string remoteFileId,
        string fileName,
        long sizeBytes,
        bool isGoogleWorkspaceDoc,
        string? mimeType)
    {
        if (!_driveClient.IsConnected)
        {
            StatusSummary = Loc.Text("ConnectBeforeSync");
            return;
        }

        TransferErrorBannerText = string.Empty;
        StatusSummary = Loc.Format("DownloadingFileFmt", fileName);
        try
        {
            var savedPath = await _syncService.DownloadRemoteFileAsync(
                remoteFileId,
                fileName,
                sizeBytes,
                isGoogleWorkspaceDoc,
                mimeType);
            StatusSummary = Loc.Format("DownloadedToFmt", savedPath);
        }
        catch (Exception ex)
        {
            var err = Loc.Format("DownloadErrorBannerFmt", fileName, ex.Message);
            TransferErrorBannerText = err;
            StatusSummary = err;
        }
    }

    private static string GetSyncFolderDisplayName(LocalSyncFolder folder)
    {
        if (!string.IsNullOrWhiteSpace(folder.CustomAlias))
        {
            return folder.CustomAlias.Trim();
        }

        var trimmed = folder.LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? folder.RemoteFolderName : name;
    }

    private async Task PopulateLocalHierarchyFactoriesAsync(
        List<Func<FileHubItemViewModel>> factories,
        IReadOnlyDictionary<string, LocalSyncFolder> syncFolders,
        bool hasNetwork,
        CultureInfo culture,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_localSyncFolderId) ||
            !syncFolders.TryGetValue(_localSyncFolderId, out var activeSyncFolder))
        {
            if (!string.IsNullOrWhiteSpace(_localSyncFolderId))
            {
                ResetLocalNavigation();
                SyncBreadcrumbsFromActiveMode();
            }

            foreach (var syncFolder in syncFolders.Values.OrderBy(GetSyncFolderDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var entries = await _repository.GetTrackedFilesByFolderAsync(syncFolder.Id, ct);
                var latestUtc = syncFolder.LastSyncedAtUtc ?? DateTimeOffset.MinValue;
                var hasAnyLocal = false;

                await Task.Run(() =>
                {
                    if (Directory.Exists(syncFolder.LocalPath))
                    {
                        hasAnyLocal = true;
                    }

                    foreach (var e in entries)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            break;
                        }

                        if (e.LocalModifiedAtUtc > latestUtc)
                        {
                            latestUtc = e.LocalModifiedAtUtc;
                        }

                        if (!hasAnyLocal && !e.IsFolder)
                        {
                            var path = Path.Combine(
                                syncFolder.LocalPath,
                                e.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(path))
                            {
                                hasAnyLocal = true;
                            }
                        }
                    }
                }, ct);

                if (!hasNetwork && !hasAnyLocal)
                {
                    continue;
                }

                var capturedFolder = syncFolder;
                var folderName = GetSyncFolderDisplayName(syncFolder);
                var modified = latestUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : latestUtc;
                factories.Add(() => CreateLocalFolderItem(
                    capturedFolder.Id,
                    string.Empty,
                    folderName,
                    capturedFolder.LocalPath,
                    capturedFolder.RemoteFolderId,
                    modified,
                    culture,
                    capturedFolder));
            }

            return;
        }

        var folderEntries = await _repository.GetTrackedFilesByFolderAsync(activeSyncFolder.Id, ct);
        var currentSubPath = _localSubPath.Trim('/');

        var (sortedFolders, sortedFiles) = await Task.Run(() =>
        {
            var folderMap = new Dictionary<string, (TrackedFileEntry? Entry, DateTimeOffset LatestModifiedUtc)>(StringComparer.Ordinal);
            var fileList = new List<(TrackedFileEntry Entry, bool ExistsLocally)>();
            var prefix = string.IsNullOrEmpty(currentSubPath) ? string.Empty : currentSubPath + "/";

            foreach (var entry in folderEntries)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var rel = entry.RelativePath.Replace('\\', '/').Trim('/');
                if (!string.IsNullOrEmpty(prefix) && !rel.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var remainder = string.IsNullOrEmpty(prefix) ? rel : rel[prefix.Length..];
                if (string.IsNullOrEmpty(remainder))
                {
                    continue;
                }

                var slashIdx = remainder.IndexOf('/');
                if (slashIdx >= 0)
                {
                    var seg = remainder[..slashIdx];
                    if (!hasNetwork)
                    {
                        var candidatePath = Path.Combine(
                            activeSyncFolder.LocalPath,
                            rel.Replace('/', Path.DirectorySeparatorChar));
                        var okOnDisk = entry.IsFolder ? Directory.Exists(candidatePath) : File.Exists(candidatePath);
                        if (!okOnDisk)
                        {
                            continue;
                        }
                    }

                    folderMap.TryGetValue(seg, out var current);
                    var nextMod = entry.LocalModifiedAtUtc > current.LatestModifiedUtc
                        ? entry.LocalModifiedAtUtc
                        : current.LatestModifiedUtc;
                    folderMap[seg] = (current.Entry, nextMod);
                }
                else if (entry.IsFolder)
                {
                    if (!hasNetwork)
                    {
                        var dirPath = Path.Combine(
                            activeSyncFolder.LocalPath,
                            rel.Replace('/', Path.DirectorySeparatorChar));
                        if (!Directory.Exists(dirPath))
                        {
                            continue;
                        }
                    }

                    folderMap.TryGetValue(remainder, out var current);
                    var nextMod = entry.LocalModifiedAtUtc > current.LatestModifiedUtc
                        ? entry.LocalModifiedAtUtc
                        : current.LatestModifiedUtc;
                    folderMap[remainder] = (entry, nextMod);
                }
                else
                {
                    var diskPath = Path.Combine(
                        activeSyncFolder.LocalPath,
                        rel.Replace('/', Path.DirectorySeparatorChar));
                    var exists = File.Exists(diskPath);
                    if (!hasNetwork && !exists)
                    {
                        continue;
                    }

                    fileList.Add((entry, exists));
                }
            }

            var orderedFolders = folderMap
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => (Name: kv.Key, kv.Value.Entry, kv.Value.LatestModifiedUtc))
                .ToList();

            var orderedFiles = fileList
                .OrderBy(p => Path.GetFileName(p.Entry.RelativePath), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (orderedFolders, orderedFiles);
        }, ct);

        foreach (var (name, folderEntry, modifiedUtc) in sortedFolders)
        {
            var nextSub = string.IsNullOrEmpty(currentSubPath) ? name : $"{currentSubPath}/{name}";
            var fullDiskPath = Path.Combine(
                activeSyncFolder.LocalPath,
                nextSub.Replace('/', Path.DirectorySeparatorChar));
            var remoteId = folderEntry?.RemoteFileId ?? string.Empty;
            var mod = modifiedUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : modifiedUtc;

            factories.Add(() => CreateLocalFolderItem(
                activeSyncFolder.Id,
                nextSub,
                name,
                fullDiskPath,
                remoteId,
                mod,
                culture));
        }

        foreach (var pair in sortedFiles)
        {
            var capturedEntry = pair.Entry;
            var capturedExists = pair.ExistsLocally;
            factories.Add(() => CreateTrackedItem(capturedEntry, syncFolders, culture, capturedExists));
        }
    }

    private FileHubItemViewModel CreateLocalFolderItem(
        string syncFolderId,
        string subPath,
        string displayName,
        string localFullPath,
        string remoteFolderId,
        DateTimeOffset modifiedUtc,
        CultureInfo culture,
        LocalSyncFolder? customizedFolder = null)
    {
        var isConnected = _driveClient.IsConnected;
        var hasRemote = isConnected && !string.IsNullOrWhiteSpace(remoteFolderId);
        var iconData = FolderCustomizationPresets.ResolveIconSvg(customizedFolder?.CustomIconKey);
        var normalizedHex = FolderCustomizationPresets.NormalizeColorHex(customizedFolder?.CustomColorHex);
        var iconBrush = !string.IsNullOrWhiteSpace(normalizedHex)
            ? SolidColorBrush.Parse(normalizedHex)
            : BrushMutedGray;

        return new FileHubItemViewModel
        {
            Name = displayName,
            PathDisplay = localFullPath,
            OriginDisplay = Loc.Text("FileHubStatusFolder"),
            OriginBrush = BrushFolderBlue,
            SizeDisplay = "—",
            ModifiedDisplay = modifiedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", culture),
            IsFolder = true,
            HasLocalFile = Directory.Exists(localFullPath),
            LocalFullPath = localFullPath,
            RemoteFileId = hasRemote ? remoteFolderId : string.Empty,
            FileIconData = iconData,
            FileIconBrush = iconBrush,
            ActionLabel = Loc.Text("FileHubOpenFolder"),
            OpenInDriveLabel = Loc.Text("OpenInDriveButton"),
            CopyNameLabel = Loc.Text("CopyNameMenu"),
            CopyLinkLabel = Loc.Text("CopyDriveLinkMenu"),
            OpenActionCommand = new AsyncRelayCommand(() => OpenLocalFolderAsync(syncFolderId, subPath, displayName)),
            OpenInDriveCommand = hasRemote
                ? new AsyncRelayCommand(() => OpenRemoteInDriveBrowserAsync(remoteFolderId, true))
                : null,
            CopyNameCommand = new AsyncRelayCommand(() => CopyToClipboardAsync(displayName)),
            CopyDriveLinkCommand = hasRemote
                ? new AsyncRelayCommand(() => CopyToClipboardAsync($"https://drive.google.com/drive/folders/{remoteFolderId}"))
                : null
        };
    }

    private async Task OpenLocalFolderAsync(string syncFolderId, string subPath, string displayName)
    {
        if (string.IsNullOrWhiteSpace(syncFolderId) ||
            (string.Equals(_localSyncFolderId, syncFolderId, StringComparison.Ordinal) &&
             string.Equals(_localSubPath, subPath, StringComparison.Ordinal)))
        {
            return;
        }

        var prevFolderId = _localSyncFolderId;
        var prevSubPath = _localSubPath;
        var prevCount = _localBreadcrumbs.Count;

        var crumbId = string.IsNullOrEmpty(subPath)
            ? $"{LocalFolderCrumbPrefix}{syncFolderId}"
            : $"{LocalFolderCrumbPrefix}{syncFolderId}|{subPath}";

        SearchText = string.Empty;
        if (!string.Equals(_localBreadcrumbs.LastOrDefault()?.FolderId, crumbId, StringComparison.Ordinal))
        {
            _localBreadcrumbs.Add(new FileHubBreadcrumbItem(crumbId, displayName, NavigateToBreadcrumbAsync));
        }

        _localSyncFolderId = syncFolderId;
        _localSubPath = subPath;
        SyncBreadcrumbsFromActiveMode();

        var ok = await RefreshInternalAsync();
        if (!ok)
        {
            while (_localBreadcrumbs.Count > prevCount)
            {
                _localBreadcrumbs.RemoveAt(_localBreadcrumbs.Count - 1);
            }

            _localSyncFolderId = prevFolderId;
            _localSubPath = prevSubPath;
            SyncBreadcrumbsFromActiveMode();
        }
    }

    private async Task OpenRemoteFolderAsync(string folderId, string folderName)
    {
        if (!_driveClient.IsConnected ||
            !_syncService.IsOnline ||
            string.IsNullOrWhiteSpace(folderId) ||
            string.Equals(_driveFolderId, folderId, StringComparison.Ordinal))
        {
            return;
        }

        var prevFolderId = _driveFolderId;
        var prevFolderName = _driveFolderName;
        var prevCount = _driveBreadcrumbs.Count;

        SearchText = string.Empty;
        if (!string.Equals(_driveBreadcrumbs.LastOrDefault()?.FolderId, folderId, StringComparison.Ordinal))
        {
            _driveBreadcrumbs.Add(new FileHubBreadcrumbItem(folderId, folderName, NavigateToBreadcrumbAsync));
        }

        _driveFolderId = folderId;
        _driveFolderName = folderName;
        SyncBreadcrumbsFromActiveMode();

        var ok = await RefreshInternalAsync();
        if (!ok)
        {
            while (_driveBreadcrumbs.Count > prevCount)
            {
                _driveBreadcrumbs.RemoveAt(_driveBreadcrumbs.Count - 1);
            }

            _driveFolderId = prevFolderId;
            _driveFolderName = prevFolderName;
            SyncBreadcrumbsFromActiveMode();
        }
    }

    private async Task OpenFileItemAsync(bool existsLocally, string? localPath, string remoteFileId)
    {
        if (existsLocally && !string.IsNullOrWhiteSpace(localPath) && OpenLocalFileCallback is not null)
        {
            await OpenLocalFileCallback.Invoke(localPath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(remoteFileId) && OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri($"https://drive.google.com/file/d/{remoteFileId}/view"));
        }
    }

    private void RebuildFilterOptions()
    {
        var savedIndex = Math.Max(0, SelectedSourceFilterIndex);
        SourceFilterOptions.Clear();
        SourceFilterOptions.Add(Loc.Text("FileHubFilterDrive"));
        SourceFilterOptions.Add(Loc.Text("FileHubFilterLocal"));
        SourceFilterOptions.Add(Loc.Text("FileHubFilterTrash"));
        SelectedSourceFilterIndex = Math.Clamp(savedIndex, 0, SourceFilterOptions.Count - 1);
    }
}
