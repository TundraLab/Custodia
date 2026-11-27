using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Converters;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;

namespace TundraDrive.Desktop.ViewModels;

public sealed record FileHubMoveDestinationItem(string TargetId, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class FileHubBreadcrumbItem
{
    public string FolderId { get; }
    public string Name { get; }
    public bool ShowSeparator { get; set; }
    public IRelayCommand SelectCommand { get; }

    public FileHubBreadcrumbItem(string folderId, string name, Func<FileHubBreadcrumbItem, Task> onSelect)
    {
        FolderId = folderId;
        Name = name;
        SelectCommand = new AsyncRelayCommand(() => onSelect(this));
    }
}

public sealed class SharePermissionItemViewModel
{
    public string PermissionId { get; }
    public string DisplayTitle { get; }
    public string RoleDisplay { get; }
    public string RevokeLabel { get; }
    public bool CanRevoke { get; }
    public IRelayCommand RevokeCommand { get; }

    public SharePermissionItemViewModel(
        string permissionId,
        string displayTitle,
        string roleDisplay,
        string revokeLabel,
        bool canRevoke,
        Func<string, Task> onRevoke)
    {
        PermissionId = permissionId;
        DisplayTitle = displayTitle;
        RoleDisplay = roleDisplay;
        RevokeLabel = revokeLabel;
        CanRevoke = canRevoke;
        RevokeCommand = new AsyncRelayCommand(() => onRevoke(PermissionId));
    }
}

public sealed partial class FileHubItemViewModel : ObservableObject, IDisposable
{
    internal static IBrush DefaultMutedBrush => ThemeAccentService.ResolveThemeBrush("#C4C6D0", "#44474F");
    private static IBrush StarGoldBrush => ThemeAccentService.ResolveThemeBrush("#F5C26B", "#9A5B00");
    private const string StarFilledIconSvg = "M 12.0,2.16 L 15.0,8.39 L 21.79,9.28 L 16.79,14.06 L 18.01,20.85 L 12.0,17.51 L 5.99,20.85 L 7.21,14.06 L 2.21,9.28 L 9.0,8.39 Z";
    private const string StarOutlineIconSvg = "M 12.0,1.36 L 15.23,8.06 L 22.56,9.02 L 17.16,14.18 L 18.49,21.52 L 12.0,17.92 L 5.51,21.52 L 6.84,14.18 L 1.44,9.02 L 8.77,8.06 Z M 10.43,10.37 L 6.94,10.82 L 9.54,13.3 L 8.91,16.77 L 12.0,15.06 L 15.09,16.77 L 14.46,13.3 L 17.06,10.82 L 13.57,10.37 L 12.0,7.11 Z";

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
    public string? ThumbnailLink { get; init; }
    public string? SyncFolderId { get; init; }
    public string? AppearanceKey { get; init; }
    public string OriginalFolderName { get; init; } = string.Empty;
    public string? CustomAlias { get; init; }
    public string? CustomColorHex { get; init; }
    public string? CustomIconKey { get; init; }
    public string ActionLabel { get; init; } = string.Empty;
    public bool CanDownload => !IsInTrash && !string.IsNullOrWhiteSpace(RemoteFileId);
    public bool CanPreview => !IsFolder;
    public bool CanMoveToTrash => !IsInTrash && !string.IsNullOrWhiteSpace(RemoteFileId);
    public bool CanCopyDriveLink => !IsInTrash && !string.IsNullOrWhiteSpace(RemoteFileId);
    public bool CanOpenNormal => !IsInTrash;
    public bool CanToggleFavorite => !IsInTrash && (!string.IsNullOrWhiteSpace(RemoteFileId) || HasLocalFile);
    public string DownloadLabel { get; init; } = string.Empty;
    public string PreviewLabel { get; init; } = string.Empty;
    public string OpenInDriveLabel { get; init; } = string.Empty;
    public string TrashLabel { get; init; } = string.Empty;
    public string RestoreLabel { get; init; } = string.Empty;
    public string DeletePermanentlyLabel { get; init; } = string.Empty;
    public string CopyNameLabel { get; init; } = string.Empty;
    public string CopyLinkLabel { get; init; } = string.Empty;
    public string AddToFavoritesLabel { get; init; } = string.Empty;
    public string RemoveFromFavoritesLabel { get; init; } = string.Empty;
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
    public IAsyncRelayCommand? ToggleFavoriteCommand { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIconData))]
    [NotifyPropertyChangedFor(nameof(FavoriteIconBrush))]
    [NotifyPropertyChangedFor(nameof(FavoriteTooltip))]
    private bool _isStarred;

    public string FavoriteIconData => IsStarred ? StarFilledIconSvg : StarOutlineIconSvg;
    public IBrush FavoriteIconBrush => IsStarred ? StarGoldBrush : DefaultMutedBrush;
    public string FavoriteTooltip => IsStarred ? RemoveFromFavoritesLabel : AddToFavoritesLabel;

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
    private static IBrush BrushMutedGray => FileHubItemViewModel.DefaultMutedBrush;
    private static IBrush BrushFolderBlue => ThemeAccentService.ResolveThemeBrush("#A8C7FA", "#0B57D0");
    private static IBrush BrushSyncedGreen => ThemeAccentService.ResolveThemeBrush("#6DD58C", "#146C2E");
    private static IBrush BrushTrashSlate => ThemeAccentService.ResolveThemeBrush("#9296A4", "#5F6368");
    private static IBrush BrushSheetGreen => ThemeAccentService.ResolveThemeBrush("#6DD58C", "#146C2E");
    private static IBrush BrushPdfRed => ThemeAccentService.ResolveThemeBrush("#F28B82", "#B3261E");
    private static IBrush BrushDocBlue => ThemeAccentService.ResolveThemeBrush("#A8C7FA", "#0B57D0");
    private static IBrush BrushSlideAmber => ThemeAccentService.ResolveThemeBrush("#F5C26B", "#9A5B00");
    private static IBrush BrushVideoCyan => ThemeAccentService.ResolveThemeBrush("#7FCFFF", "#006495");
    private static IBrush BrushAudioPurple => ThemeAccentService.ResolveThemeBrush("#D0BCFF", "#6750A4");
    private static IBrush BrushImagePink => ThemeAccentService.ResolveThemeBrush("#F6A5C0", "#B32645");
    private const string GoogleOneStorageUrl = "https://one.google.com/storage";

    private static readonly string FolderIconSvg = FolderCustomizationPresets.ResolveIconSvg(FolderCustomizationPresets.DefaultIconKey);
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
    private readonly IAppSettingsService? _settingsService;
    private EventHandler? _themeChangedHandler;
    private CancellationTokenSource? _previewCts;
    private Task? _inFlightPreviewTask;
    private CancellationTokenSource? _thumbnailCts;
    private CancellationTokenSource? _refreshCts;
    private FileHubItemViewModel? _previewedItem;
    private string _previewLocalFilePath = string.Empty;
    private DriveAccount _lastAccount = new();
    private const int ItemsPerPage = 48;
    private const string LocalRootCrumbId = "__local_root__";
    private const string SharedRootCrumbId = "__shared_root__";
    private const string LocalFolderCrumbPrefix = "local:";
    private const string DriveFavoritePrefix = "drive:";
    private const string LocalFavoritePrefix = "local:";
    private readonly List<FileHubBreadcrumbItem> _driveBreadcrumbs = [];
    private readonly List<FileHubBreadcrumbItem> _localBreadcrumbs = [];
    private readonly List<FileHubBreadcrumbItem> _sharedBreadcrumbs = [];
    private readonly HashSet<string> _knownStarredRemoteIds = new(StringComparer.Ordinal);
    private readonly Lock _refreshApplyLock = new();
    private bool _hasLoadedRemoteStarredIds;
    private string _driveFolderId = "root";
    private string _driveFolderName = string.Empty;
    private string _sharedFolderId = SharedRootCrumbId;
    private string _sharedFolderName = string.Empty;
    private string? _localSyncFolderId;
    private string _localSubPath = string.Empty;
    private IReadOnlyList<Func<FileHubItemViewModel>> _allPageFactories = [];
    private string _activePageContextLabel = string.Empty;
    private bool _isBatchUpdatingFiles;
    private bool _suppressNextFilterRefresh;

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
    public ObservableCollection<FileHubItemViewModel> GridFolders { get; } = [];
    public ObservableCollection<FileHubItemViewModel> GridFileItems { get; } = [];
    public bool HasGridFolders => GridFolders.Count > 0;
    public bool HasGridFiles => GridFileItems.Count > 0;

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
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    [NotifyPropertyChangedFor(nameof(CanPreviewSelected))]
    [NotifyPropertyChangedFor(nameof(CanCustomizeSelectedFolder))]
    [NotifyPropertyChangedFor(nameof(CanRenameSelected))]
    [NotifyPropertyChangedFor(nameof(CanMoveSelected))]
    [NotifyPropertyChangedFor(nameof(CanOpenSelectedInDrive))]
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
    [NotifyPropertyChangedFor(nameof(ShowFloatingSelectionBar))]
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
    private bool _isRenameOpen;

    [ObservableProperty]
    private string _renameNewName = string.Empty;

    [ObservableProperty]
    private bool _isMoveOpen;

    [ObservableProperty]
    private int _selectedMoveDestinationIndex;

    public ObservableCollection<FileHubMoveDestinationItem> MoveDestinations { get; } = [];

    [ObservableProperty]
    private bool _isCustomizeFolderOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShareLink))]
    private bool _isShareOpen;

    [ObservableProperty]
    private bool _isShareBusy;

    [ObservableProperty]
    private string _shareTargetTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShareLink))]
    private string _shareLinkUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShareLink))]
    private string _shareAnyoneRole = "none";

    [ObservableProperty]
    private string _shareInviteEmail = string.Empty;

    [ObservableProperty]
    private int _selectedShareInviteRoleIndex;

    [ObservableProperty]
    private string _shareFeedbackMessage = string.Empty;

    private string? _shareTargetRemoteId;
    private string? _shareTargetName;
    public ObservableCollection<SharePermissionItemViewModel> SharePermissions { get; } = [];
    public ObservableCollection<string> ShareRoleOptions { get; } = [];
    public bool HasShareLink => !string.IsNullOrWhiteSpace(ShareLinkUrl) && ShareAnyoneRole is "reader" or "writer";
    public string ShareAnyoneStatusText => ShareAnyoneRole switch
    {
        "writer" => Loc.Format("ShareLinkActiveHint", Loc.Text("ShareRoleWriter")),
        "reader" => Loc.Format("ShareLinkActiveHint", Loc.Text("ShareRoleReader")),
        _ => Loc.Text("ShareLinkInactiveHint")
    };

    [ObservableProperty]
    private string _customizeTargetTitle = string.Empty;

    [ObservableProperty]
    private string _customizeEditAlias = string.Empty;

    [ObservableProperty]
    private string _customizeEditColorHex = FolderCustomizationPresets.DefaultColorHex;

    [ObservableProperty]
    private string _customizeEditIconKey = FolderCustomizationPresets.DefaultIconKey;

    private string? _customizeTargetSyncFolderId;
    private string? _customizeTargetAppearanceKey;
    public ObservableCollection<FolderColorChoice> CustomizeColorChoices { get; } = [];
    public ObservableCollection<FolderIconChoice> CustomizeIconChoices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTransferErrorBanner))]
    private string _transferErrorBannerText = string.Empty;

    public bool HasTransferErrorBanner => !string.IsNullOrWhiteSpace(TransferErrorBannerText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFile))]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    [NotifyPropertyChangedFor(nameof(ShowFloatingSelectionBar))]
    [NotifyPropertyChangedFor(nameof(HasMultipleSelected))]
    [NotifyPropertyChangedFor(nameof(CanPreviewSelected))]
    [NotifyPropertyChangedFor(nameof(CanCustomizeSelectedFolder))]
    [NotifyPropertyChangedFor(nameof(CanRenameSelected))]
    [NotifyPropertyChangedFor(nameof(CanMoveSelected))]
    [NotifyPropertyChangedFor(nameof(CanDownloadSelected))]
    [NotifyPropertyChangedFor(nameof(CanOpenSelectedInDrive))]
    [NotifyPropertyChangedFor(nameof(CanShareSelected))]
    [NotifyPropertyChangedFor(nameof(CanToggleFavoriteSelected))]
    [NotifyPropertyChangedFor(nameof(FavoriteSelectedLabel))]
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
    public bool IsFavoritesView => SelectedSourceFilterIndex == 3;
    public bool IsSharedWithMeView => SelectedSourceFilterIndex == 4;
    public string TrashTabDisplay => string.IsNullOrWhiteSpace(TrashQuotaBadge)
        ? Loc.TrashTabButton
        : $"{Loc.TrashTabButton} ({TrashQuotaBadge})";
    public string RootBreadcrumbLabel => IsLocalFilter
        ? Loc.Text("FileHubFilterLocal")
        : IsSharedWithMeView
            ? Loc.Text("SharedWithMeRootLabel")
            : Loc.Text("MyDriveLabel");
    public bool CanNavigateBack => (SelectedSourceFilterIndex is 0 or 1 or 4) && Breadcrumbs.Count > 1;
    public bool HasSelectedFile => GetEffectiveSelectedItems().Any(i => !i.IsFolder);
    public bool HasSelectedItem => SelectedCount > 0 || SelectedItem is not null;
    public bool ShowSelectionBar => !IsTrashView && HasSelectedItem;
    public bool ShowFloatingSelectionBar => !IsTrashView && HasSelectedItem && (HasMultipleSelected || !IsPreviewVisible);
    public bool ShowTrashSelectionActions => IsTrashView && HasSelectedItem;
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public bool HasMultipleSelected => SelectedCount > 1;
    public bool CanPreviewSelected => SelectedCount <= 1 && GetSingleEffectiveSelectedItem() is { IsFolder: false };
    public bool CanCustomizeSelectedFolder => SelectedCount <= 1 && GetSingleEffectiveSelectedItem() is { IsFolder: true, IsInTrash: false };
    public bool CanRenameSelected => !IsTrashView && SelectedCount <= 1 && GetSingleEffectiveSelectedItem() is { } single &&
                                     (!string.IsNullOrWhiteSpace(single.RemoteFileId) || single.HasLocalFile);
    public bool CanMoveSelected => !IsTrashView && GetEffectiveSelectedItems().Any(i =>
                                     !string.IsNullOrWhiteSpace(i.RemoteFileId) || i.HasLocalFile);
    public bool CanDownloadSelected => GetEffectiveSelectedItems().Any(i => i.CanDownload);
    public bool CanOpenSelectedInDrive => GetSingleEffectiveSelectedItem() is { } single && !string.IsNullOrWhiteSpace(single.RemoteFileId);
    public bool CanShareSelected => !IsTrashView && SelectedCount <= 1 && GetSingleEffectiveSelectedItem() is { } single &&
                                    !string.IsNullOrWhiteSpace(single.RemoteFileId);
    public bool CanToggleFavoriteSelected => !IsTrashView && GetEffectiveSelectedItems().Any(i => i.CanToggleFavorite);
    public string FavoriteSelectedLabel
    {
        get
        {
            var eligible = GetEffectiveSelectedItems().Where(i => i.CanToggleFavorite).ToList();
            return eligible.Count > 0 && eligible.All(i => i.IsStarred)
                ? Loc.Text("RemoveFromFavoritesButton")
                : Loc.Text("AddToFavoritesButton");
        }
    }
    public bool CanTrashSelected => !IsTrashView && GetEffectiveSelectedItems().Any(i => i.CanMoveToTrash);
    public bool CanRestoreSelected => IsTrashView && GetEffectiveSelectedItems().Any(i => i.IsInTrash);
    public bool CanDeleteSelectedPermanently => IsTrashView && GetEffectiveSelectedItems().Any(i => i.IsInTrash);
    public string SelectionCountDisplay => SelectedCount > 0 ? Loc.Format("FileHubSelectedCountFmt", SelectedCount) : string.Empty;
    public bool CanEmptyTrash => IsTrashView && IsAccountConnected && Files.Count > 0 && !IsLoading;
    public string EmptyStateText => IsTrashView
        ? Loc.Text("TrashEmptyHint")
        : IsFavoritesView
            ? Loc.Text("FavoritesEmptyHint")
            : IsSharedWithMeView
                ? Loc.Text("SharedEmptyHint")
                : !IsAccountConnected && IsDriveFilter
                    ? Loc.Text("ConnectHintSubtitle")
                    : Loc.Text("FileHubEmptyFolder");
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
    public Func<string, Task<string?>>? PickSaveFileCallback { get; set; }
    public Func<Task<string?>>? PickDownloadFolderCallback { get; set; }
    public Func<string, Task>? OpenLocalFileCallback { get; set; }
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Func<string, Task>? CopyTextCallback { get; set; }

    public FileHubViewModel(
        ISyncStateRepository repository,
        IGoogleDriveClient driveClient,
        IDriveSyncService syncService,
        FilePreviewService previewService,
        ILocalizationService localization,
        IAppSettingsService? settingsService = null)
    {
        _repository = repository;
        _driveClient = driveClient;
        _syncService = syncService;
        _previewService = previewService;
        _settingsService = settingsService;
        Loc = localization;

        if (_settingsService?.Current.FavoriteKeys is { Count: > 0 } savedFavorites)
        {
            foreach (var key in savedFavorites)
            {
                if (key.StartsWith(DriveFavoritePrefix, StringComparison.Ordinal))
                {
                    var remoteId = key[DriveFavoritePrefix.Length..];
                    if (!string.IsNullOrWhiteSpace(remoteId))
                    {
                        _knownStarredRemoteIds.Add(remoteId);
                    }
                }
            }
        }

        _driveFolderId = "root";
        _driveFolderName = Loc.Text("MyDriveLabel");
        _driveBreadcrumbs.Add(new FileHubBreadcrumbItem("root", _driveFolderName, NavigateToBreadcrumbAsync));
        _localBreadcrumbs.Add(new FileHubBreadcrumbItem(LocalRootCrumbId, Loc.Text("FileHubFilterLocal"), NavigateToBreadcrumbAsync));
        _sharedFolderId = SharedRootCrumbId;
        _sharedFolderName = Loc.Text("SharedWithMeRootLabel");
        _sharedBreadcrumbs.Add(new FileHubBreadcrumbItem(SharedRootCrumbId, _sharedFolderName, NavigateToBreadcrumbAsync));
        Files.CollectionChanged += (_, _) =>
        {
            if (!_isBatchUpdatingFiles)
            {
                SyncGridSections();
            }
        };
        SyncBreadcrumbsFromActiveMode();
        RebuildFilterOptions();
        RebuildShareRoleOptions();
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

            if (_sharedBreadcrumbs.Count > 0 && _sharedBreadcrumbs[0].FolderId == SharedRootCrumbId)
            {
                _sharedBreadcrumbs[0] = new FileHubBreadcrumbItem(SharedRootCrumbId, Loc.Text("SharedWithMeRootLabel"), NavigateToBreadcrumbAsync);
                if (_sharedFolderId == SharedRootCrumbId)
                {
                    _sharedFolderName = Loc.Text("SharedWithMeRootLabel");
                }
            }

            RebuildShareRoleOptions();
            OnPropertyChanged(nameof(ShareAnyoneStatusText));

            SyncBreadcrumbsFromActiveMode();
            RebuildFilterOptions();
            ApplyAccount(_lastAccount);
            OnPropertyChanged(nameof(PdfPageDisplay));
            OnPropertyChanged(nameof(AudioToggleLabel));
            OnPropertyChanged(nameof(TrashTabDisplay));
            OnPropertyChanged(nameof(EmptyStateText));
            _ = RefreshAsync();
        };

        _themeChangedHandler = (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_allPageFactories.Count > 0)
                {
                    var hadPreview = IsPreviewVisible;
                    var selectedIds = SelectedItems
                        .Select(x => x.RemoteFileId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .ToHashSet(StringComparer.Ordinal);
                    ApplyPage(CurrentPage);
                    if (selectedIds.Count > 0)
                    {
                        var restored = Files
                            .Where(f => !string.IsNullOrWhiteSpace(f.RemoteFileId) && selectedIds.Contains(f.RemoteFileId))
                            .ToList();
                        if (restored.Count > 0)
                        {
                            UpdateSelectedItems(restored);
                        }
                    }

                    if (!hadPreview && IsPreviewVisible)
                    {
                        IsPreviewVisible = false;
                    }
                }

                if (IsCustomizeFolderOpen)
                {
                    RebuildCustomizeChoices();
                }
            });
        };
        ThemeAccentService.ThemeChanged += _themeChangedHandler;
    }

    public bool ShowBreadcrumbs => SelectedSourceFilterIndex is 0 or 1 or 4 && string.IsNullOrWhiteSpace(SearchText);

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

        // Keep SelectedItem aligned with checkbox and multi-selection state without breaking hidden ListBox switches.
        var nextSelected = next.Count == 1
            ? next[0]
            : next.Count == 0
                ? null
                : SelectedItem is not null && nextSet.Contains(SelectedItem)
                    ? SelectedItem
                    : next[^1];

        if (!ReferenceEquals(SelectedItem, nextSelected))
        {
            SelectedItem = nextSelected;
        }

        NotifySelectionStateChanged();
    }

    private FileHubItemViewModel? GetSingleEffectiveSelectedItem()
    {
        if (SelectedItems.Count > 1)
        {
            return null;
        }

        if (SelectedItems.Count == 1)
        {
            return SelectedItems[0];
        }

        return SelectedItem;
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
        OnPropertyChanged(nameof(ShowSelectionBar));
        OnPropertyChanged(nameof(ShowFloatingSelectionBar));
        OnPropertyChanged(nameof(ShowTrashSelectionActions));
        OnPropertyChanged(nameof(HasMultipleSelected));
        OnPropertyChanged(nameof(CanPreviewSelected));
        OnPropertyChanged(nameof(CanCustomizeSelectedFolder));
        OnPropertyChanged(nameof(CanRenameSelected));
        OnPropertyChanged(nameof(CanMoveSelected));
        OnPropertyChanged(nameof(CanDownloadSelected));
        OnPropertyChanged(nameof(CanOpenSelectedInDrive));
        OnPropertyChanged(nameof(CanShareSelected));
        OnPropertyChanged(nameof(CanToggleFavoriteSelected));
        OnPropertyChanged(nameof(FavoriteSelectedLabel));
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
        OnPropertyChanged(nameof(IsFavoritesView));
        OnPropertyChanged(nameof(IsSharedWithMeView));
        OnPropertyChanged(nameof(ShowSelectionBar));
        OnPropertyChanged(nameof(ShowFloatingSelectionBar));
        OnPropertyChanged(nameof(ShowTrashSelectionActions));
        OnPropertyChanged(nameof(TrashTabDisplay));
        OnPropertyChanged(nameof(CanEmptyTrash));
        OnPropertyChanged(nameof(CanShareSelected));
        OnPropertyChanged(nameof(CanToggleFavoriteSelected));
        OnPropertyChanged(nameof(FavoriteSelectedLabel));
        OnPropertyChanged(nameof(CanTrashSelected));
        OnPropertyChanged(nameof(CanRenameSelected));
        OnPropertyChanged(nameof(CanMoveSelected));
        OnPropertyChanged(nameof(CanRestoreSelected));
        OnPropertyChanged(nameof(CanDeleteSelectedPermanently));
        OnPropertyChanged(nameof(EmptyStateText));
        if (value >= 0 && !_suppressNextFilterRefresh)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowBreadcrumbs));
        OnPropertyChanged(nameof(HasSearchText));
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        SearchText = string.Empty;
        await RefreshAsync();
    }

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
        var target = GetSingleEffectiveSelectedItem();
        if (target is { IsFolder: false })
        {
            await LoadPreviewAsync(target, 1);
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

        TransferErrorBannerText = string.Empty;
        IsLoading = true;
        try
        {
            var filesToDownload = await ExpandRemoteDownloadTargetsAsync(targets);
            if (filesToDownload.Count == 0)
            {
                return;
            }

            if (filesToDownload.Count == 1)
            {
                var single = filesToDownload[0];
                await DownloadFileAsync(single.RemoteFileId, single.Name, single.SizeBytes, single.IsGoogleWorkspaceDoc, single.MimeType);
                return;
            }

            var total = filesToDownload.Count;
            var completed = 0;
            var downloadedCount = 0;
            var concurrencyLimit = GetEffectiveTransferConcurrencyLimit();
            using var gate = concurrencyLimit is int limit ? new SemaphoreSlim(limit, limit) : null;

            var tasks = filesToDownload.Select(async item =>
            {
                if (gate is not null)
                {
                    await gate.WaitAsync();
                }

                try
                {
                    var current = Interlocked.Increment(ref completed);
                    Dispatcher.UIThread.Post(() =>
                        StatusSummary = $"({current}/{total}) {Loc.Format("DownloadingFileFmt", item.Name)}");

                    await _syncService.DownloadRemoteFileAsync(
                        item.RemoteFileId,
                        item.Name,
                        item.SizeBytes,
                        item.IsGoogleWorkspaceDoc,
                        item.MimeType);
                    Interlocked.Increment(ref downloadedCount);
                }
                catch (Exception ex)
                {
                    var err = Loc.Format("DownloadErrorBannerFmt", item.Name, ex.Message);
                    Dispatcher.UIThread.Post(() =>
                    {
                        TransferErrorBannerText = err;
                        StatusSummary = err;
                    });
                }
                finally
                {
                    gate?.Release();
                }
            });

            await Task.WhenAll(tasks);

            if (downloadedCount > 0)
            {
                StatusSummary = Loc.Format("FilesDownloadedBatchFmt", downloadedCount);
            }
        }
        catch (Exception ex)
        {
            var err = Loc.Format("FileHubErrorFmt", ex.Message);
            TransferErrorBannerText = err;
            StatusSummary = err;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedAsAsync()
    {
        var targets = GetEffectiveSelectedItems()
            .Where(i => i.CanDownload && !string.IsNullOrWhiteSpace(i.RemoteFileId))
            .ToList();
        if (targets.Count == 0)
        {
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

        var singleFile = targets.Count == 1 && !targets[0].IsFolder ? targets[0] : null;
        string? explicitFilePath = null;
        string? explicitFolderPath = null;

        if (singleFile is not null)
        {
            if (PickSaveFileCallback is null)
            {
                return;
            }

            explicitFilePath = await PickSaveFileCallback.Invoke(singleFile.Name);
            if (string.IsNullOrWhiteSpace(explicitFilePath))
            {
                return;
            }
        }
        else
        {
            if (PickDownloadFolderCallback is null)
            {
                return;
            }

            explicitFolderPath = await PickDownloadFolderCallback.Invoke();
            if (string.IsNullOrWhiteSpace(explicitFolderPath))
            {
                return;
            }
        }

        TransferErrorBannerText = string.Empty;
        IsLoading = true;
        try
        {
            if (singleFile is not null && !string.IsNullOrWhiteSpace(explicitFilePath))
            {
                StatusSummary = Loc.Format("DownloadingFileFmt", singleFile.Name);
                var savedPath = await _syncService.DownloadRemoteFileToAsync(
                    singleFile.RemoteFileId,
                    singleFile.Name,
                    explicitFilePath,
                    treatAsFullFilePath: true,
                    singleFile.SizeBytes,
                    singleFile.IsGoogleWorkspaceDoc,
                    singleFile.MimeType);
                StatusSummary = Loc.Format("SaveAsSuccessFmt", savedPath);
                return;
            }

            var filesToDownload = await ExpandRemoteDownloadTargetsAsync(targets);
            if (filesToDownload.Count == 0 || string.IsNullOrWhiteSpace(explicitFolderPath))
            {
                return;
            }

            var total = filesToDownload.Count;
            var completed = 0;
            var downloadedCount = 0;
            var concurrencyLimit = GetEffectiveTransferConcurrencyLimit();
            using var gate = concurrencyLimit is int limit ? new SemaphoreSlim(limit, limit) : null;

            var tasks = filesToDownload.Select(async item =>
            {
                if (gate is not null)
                {
                    await gate.WaitAsync();
                }

                try
                {
                    var current = Interlocked.Increment(ref completed);
                    Dispatcher.UIThread.Post(() =>
                        StatusSummary = $"({current}/{total}) {Loc.Format("DownloadingFileFmt", item.Name)}");

                    await _syncService.DownloadRemoteFileToAsync(
                        item.RemoteFileId,
                        item.Name,
                        explicitFolderPath,
                        treatAsFullFilePath: false,
                        item.SizeBytes,
                        item.IsGoogleWorkspaceDoc,
                        item.MimeType);
                    Interlocked.Increment(ref downloadedCount);
                }
                catch (Exception ex)
                {
                    var err = Loc.Format("DownloadErrorBannerFmt", item.Name, ex.Message);
                    Dispatcher.UIThread.Post(() =>
                    {
                        TransferErrorBannerText = err;
                        StatusSummary = err;
                    });
                }
                finally
                {
                    gate?.Release();
                }
            });

            await Task.WhenAll(tasks);

            if (downloadedCount > 0)
            {
                StatusSummary = Loc.Format("FilesDownloadedBatchFmt", downloadedCount);
            }
        }
        catch (Exception ex)
        {
            var err = Loc.Format("FileHubErrorFmt", ex.Message);
            TransferErrorBannerText = err;
            StatusSummary = err;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<List<FileHubItemViewModel>> ExpandRemoteDownloadTargetsAsync(IEnumerable<FileHubItemViewModel> targets)
    {
        var results = new List<FileHubItemViewModel>();
        var seenFileIds = new HashSet<string>(StringComparer.Ordinal);
        var visitedFolderIds = new HashSet<string>(StringComparer.Ordinal);

        async Task AppendFolderChildrenAsync(string folderId)
        {
            if (!visitedFolderIds.Add(folderId))
            {
                return;
            }

            var children = await _driveClient.ListChildrenAsync(folderId);
            foreach (var child in children)
            {
                if (child.IsFolder)
                {
                    await AppendFolderChildrenAsync(child.Id);
                }
                else if (seenFileIds.Add(child.Id))
                {
                    results.Add(new FileHubItemViewModel
                    {
                        Name = child.Name,
                        RemoteFileId = child.Id,
                        SizeBytes = child.SizeBytes,
                        IsGoogleWorkspaceDoc = child.IsGoogleWorkspaceDoc,
                        MimeType = child.MimeType
                    });
                }
            }
        }

        foreach (var item in targets)
        {
            if (item.IsFolder)
            {
                await AppendFolderChildrenAsync(item.RemoteFileId);
            }
            else if (seenFileIds.Add(item.RemoteFileId))
            {
                results.Add(item);
            }
        }

        return results;
    }

    [RelayCommand]
    private async Task OpenSelectedInDriveAsync()
    {
        var target = GetSingleEffectiveSelectedItem();
        if (target is null || string.IsNullOrWhiteSpace(target.RemoteFileId))
        {
            return;
        }

        await OpenRemoteInDriveBrowserAsync(target.RemoteFileId, target.IsFolder);
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
    private void SelectFavoritesFilter() => SelectedSourceFilterIndex = 3;

    [RelayCommand]
    private void SelectSharedWithMeFilter() => SelectedSourceFilterIndex = 4;

    private void RebuildShareRoleOptions()
    {
        var previous = SelectedShareInviteRoleIndex;
        ShareRoleOptions.Clear();
        ShareRoleOptions.Add(Loc.Text("ShareRoleReader"));
        ShareRoleOptions.Add(Loc.Text("ShareRoleWriter"));
        SelectedShareInviteRoleIndex = Math.Clamp(previous, 0, Math.Max(0, ShareRoleOptions.Count - 1));
    }

    [RelayCommand]
    private async Task OpenShareAsync()
    {
        var target = GetSingleEffectiveSelectedItem();
        if (target is null || string.IsNullOrWhiteSpace(target.RemoteFileId) || IsTrashView)
        {
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

        IsCreateFolderOpen = false;
        IsRenameOpen = false;
        IsMoveOpen = false;
        IsCustomizeFolderOpen = false;
        _shareTargetRemoteId = target.RemoteFileId;
        _shareTargetName = target.Name;
        ShareTargetTitle = target.Name;
        ShareInviteEmail = string.Empty;
        ShareFeedbackMessage = string.Empty;
        ShareLinkUrl = string.Empty;
        ShareAnyoneRole = "none";
        SharePermissions.Clear();
        IsShareOpen = true;
        OnPropertyChanged(nameof(ShareAnyoneStatusText));

        await ReloadShareStateAsync();
    }

    [RelayCommand]
    private void CloseShare() => IsShareOpen = false;

    [RelayCommand]
    private async Task EnableShareLinkReadAsync() => await SetAnyoneShareRoleAsync(canEdit: false);

    [RelayCommand]
    private async Task EnableShareLinkEditAsync() => await SetAnyoneShareRoleAsync(canEdit: true);

    [RelayCommand]
    private async Task RevokeShareLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(_shareTargetRemoteId))
        {
            return;
        }

        IsShareBusy = true;
        ShareFeedbackMessage = string.Empty;
        try
        {
            await _syncService.DisableAnyoneLinkAsync(_shareTargetRemoteId, _shareTargetName ?? string.Empty);
            await ReloadShareStateAsync();
        }
        catch (Exception ex)
        {
            ShareFeedbackMessage = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsShareBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyShareLinkAsync()
    {
        if (string.IsNullOrWhiteSpace(ShareLinkUrl))
        {
            return;
        }

        await CopyToClipboardAsync(ShareLinkUrl);
        ShareFeedbackMessage = Loc.Text("ShareLinkCopiedMessage");
    }

    [RelayCommand]
    private async Task InviteShareEmailAsync()
    {
        if (string.IsNullOrWhiteSpace(_shareTargetRemoteId))
        {
            return;
        }

        var email = ShareInviteEmail.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || !email.Contains('.'))
        {
            ShareFeedbackMessage = Loc.Text("ShareInvalidEmailError");
            return;
        }

        IsShareBusy = true;
        ShareFeedbackMessage = string.Empty;
        try
        {
            var canEdit = SelectedShareInviteRoleIndex == 1;
            await _syncService.ShareWithEmailAsync(_shareTargetRemoteId, _shareTargetName ?? string.Empty, email, canEdit);
            ShareInviteEmail = string.Empty;
            await ReloadShareStateAsync();
        }
        catch (Exception ex)
        {
            ShareFeedbackMessage = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsShareBusy = false;
        }
    }

    private async Task SetAnyoneShareRoleAsync(bool canEdit)
    {
        if (string.IsNullOrWhiteSpace(_shareTargetRemoteId))
        {
            return;
        }

        IsShareBusy = true;
        ShareFeedbackMessage = string.Empty;
        try
        {
            var link = await _syncService.EnableAnyoneLinkAsync(_shareTargetRemoteId, _shareTargetName ?? string.Empty, canEdit);
            ShareLinkUrl = link;
            await CopyToClipboardAsync(link);
            await ReloadShareStateAsync();
            ShareFeedbackMessage = Loc.Text("ShareLinkCopiedMessage");
        }
        catch (Exception ex)
        {
            ShareFeedbackMessage = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsShareBusy = false;
        }
    }

    private async Task RevokePermissionByIdAsync(string permissionId)
    {
        if (string.IsNullOrWhiteSpace(_shareTargetRemoteId) || string.IsNullOrWhiteSpace(permissionId))
        {
            return;
        }

        IsShareBusy = true;
        ShareFeedbackMessage = string.Empty;
        try
        {
            await _syncService.RevokeRemotePermissionAsync(_shareTargetRemoteId, _shareTargetName ?? string.Empty, permissionId);
            await ReloadShareStateAsync();
        }
        catch (Exception ex)
        {
            ShareFeedbackMessage = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsShareBusy = false;
        }
    }

    private async Task ReloadShareStateAsync()
    {
        if (string.IsNullOrWhiteSpace(_shareTargetRemoteId))
        {
            return;
        }

        IsShareBusy = true;
        try
        {
            var link = await _driveClient.GetShareLinkAsync(_shareTargetRemoteId);
            var perms = await _syncService.GetRemotePermissionsAsync(_shareTargetRemoteId);

            ShareLinkUrl = link ?? $"https://drive.google.com/file/d/{Uri.EscapeDataString(_shareTargetRemoteId)}/view";
            var anyone = perms.FirstOrDefault(p => string.Equals(p.Type, "anyone", StringComparison.OrdinalIgnoreCase));
            ShareAnyoneRole = anyone is not null
                ? (string.Equals(anyone.Role, "writer", StringComparison.OrdinalIgnoreCase) ? "writer" : "reader")
                : "none";
            OnPropertyChanged(nameof(ShareAnyoneStatusText));

            SharePermissions.Clear();
            foreach (var perm in perms)
            {
                var isOwner = string.Equals(perm.Role, "owner", StringComparison.OrdinalIgnoreCase);
                var title = string.Equals(perm.Type, "anyone", StringComparison.OrdinalIgnoreCase)
                    ? Loc.Text("ShareAnyoneSectionTitle")
                    : !string.IsNullOrWhiteSpace(perm.EmailAddress)
                        ? (string.IsNullOrWhiteSpace(perm.DisplayName) ? perm.EmailAddress : $"{perm.DisplayName} ({perm.EmailAddress})")
                        : (perm.DisplayName ?? perm.Type);
                var roleDisplay = perm.Role switch
                {
                    "owner" => Loc.Text("ShareRoleOwner"),
                    "writer" => Loc.Text("ShareRoleWriter"),
                    _ => Loc.Text("ShareRoleReader")
                };

                SharePermissions.Add(new SharePermissionItemViewModel(
                    perm.Id,
                    title,
                    roleDisplay,
                    Loc.Text("ShareRevokePermissionButton"),
                    !isOwner,
                    RevokePermissionByIdAsync));
            }
        }
        catch (Exception ex)
        {
            ShareFeedbackMessage = Loc.Format("FileHubErrorFmt", ex.Message);
        }
        finally
        {
            IsShareBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleTrashView()
    {
        SelectedSourceFilterIndex = IsTrashView ? 0 : 2;
    }

    [RelayCommand]
    private async Task ToggleFavoriteSelectedAsync()
    {
        var targets = GetEffectiveSelectedItems()
            .Where(i => i.CanToggleFavorite)
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var targetStarred = targets.Any(i => !i.IsStarred);
        await ApplyFavoriteStateAsync(targets, targetStarred);
    }

    private Task ToggleItemFavoriteAsync(FileHubItemViewModel item)
    {
        if (!item.CanToggleFavorite)
        {
            return Task.CompletedTask;
        }

        return ApplyFavoriteStateAsync([item], !item.IsStarred);
    }

    private async Task ApplyFavoriteStateAsync(IReadOnlyList<FileHubItemViewModel> targets, bool targetStarred)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var hasRemoteTargets = targets.Any(t => !string.IsNullOrWhiteSpace(t.RemoteFileId) && _driveClient.IsConnected);
        if (hasRemoteTargets && !_syncService.IsOnline)
        {
            StatusSummary = Loc.Text("OfflineModeNotice");
            return;
        }

        IsLoading = hasRemoteTargets;
        var updatedCount = 0;
        var settingsChanged = false;
        try
        {
            foreach (var item in targets)
            {
                if (!string.IsNullOrWhiteSpace(item.RemoteFileId) && _driveClient.IsConnected)
                {
                    await _driveClient.SetStarredAsync(item.RemoteFileId, targetStarred);
                    if (targetStarred)
                    {
                        _knownStarredRemoteIds.Add(item.RemoteFileId);
                    }
                    else
                    {
                        _knownStarredRemoteIds.Remove(item.RemoteFileId);
                    }

                    settingsChanged |= SetFavoriteKeyInSettings(DriveFavoritePrefix + item.RemoteFileId, targetStarred);
                }
                else if (!string.IsNullOrWhiteSpace(item.LocalFullPath))
                {
                    settingsChanged |= SetFavoriteKeyInSettings(
                        LocalFavoritePrefix + NormalizeLocalFavoritePath(item.LocalFullPath),
                        targetStarred);
                }
                else
                {
                    continue;
                }

                item.IsStarred = targetStarred;
                updatedCount++;
            }

            if (settingsChanged && _settingsService is not null)
            {
                await _settingsService.SaveSilentlyAsync(_settingsService.Current);
            }

            if (updatedCount > 0 && IsFavoritesView && !targetStarred)
            {
                IsLoading = false;
                await RefreshAsync();
            }

            if (updatedCount == 1)
            {
                StatusSummary = Loc.Format(
                    targetStarred ? "ItemAddedToFavoritesFmt" : "ItemRemovedFromFavoritesFmt",
                    targets[0].Name);
            }
            else if (updatedCount > 1)
            {
                StatusSummary = Loc.Format(
                    targetStarred ? "FilesAddedToFavoritesBatchFmt" : "FilesRemovedFromFavoritesBatchFmt",
                    updatedCount);
            }

            NotifySelectionStateChanged();
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

    private bool SetFavoriteKeyInSettings(string key, bool add)
    {
        if (_settingsService is null || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var keys = _settingsService.Current.FavoriteKeys;
        var existingIndex = keys.FindIndex(k => string.Equals(k, key, StringComparison.Ordinal));
        if (add)
        {
            if (existingIndex >= 0)
            {
                return false;
            }

            keys.Add(key);
            return true;
        }

        if (existingIndex < 0)
        {
            return false;
        }

        keys.RemoveAt(existingIndex);
        return true;
    }

    private bool IsItemStarred(string? remoteFileId, string? localFullPath)
    {
        if (!string.IsNullOrWhiteSpace(remoteFileId) && _knownStarredRemoteIds.Contains(remoteFileId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(localFullPath) && _settingsService?.Current.FavoriteKeys is { Count: > 0 } keys)
        {
            var localKey = LocalFavoritePrefix + NormalizeLocalFavoritePath(localFullPath);
            return keys.Any(k => string.Equals(k, localKey, StringComparison.Ordinal));
        }

        return false;
    }

    private static string NormalizeLocalFavoritePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
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
        if (Files.Count == 0 && GridFolders.Count == 0 && GridFileItems.Count == 0)
        {
            return;
        }

        var oldItems = Files.ToList();
        _isBatchUpdatingFiles = true;
        try
        {
            Files.Clear();
            GridFolders.Clear();
            GridFileItems.Clear();
        }
        finally
        {
            _isBatchUpdatingFiles = false;
        }

        OnPropertyChanged(nameof(HasGridFolders));
        OnPropertyChanged(nameof(HasGridFiles));

        foreach (var old in oldItems)
        {
            old.Dispose();
        }
    }

    private void SyncGridSections()
    {
        GridFolders.Clear();
        GridFileItems.Clear();
        foreach (var item in Files)
        {
            if (item.IsFolder)
            {
                GridFolders.Add(item);
            }
            else
            {
                GridFileItems.Add(item);
            }
        }

        OnPropertyChanged(nameof(HasGridFolders));
        OnPropertyChanged(nameof(HasGridFiles));
    }

    public void Dispose()
    {
        var refreshCts = Interlocked.Exchange(ref _refreshCts, null);
        if (refreshCts is not null)
        {
            try
            {
                refreshCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                refreshCts.Dispose();
            }
        }

        if (_themeChangedHandler is not null)
        {
            ThemeAccentService.ThemeChanged -= _themeChangedHandler;
            _themeChangedHandler = null;
        }

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
        else if (SelectedSourceFilterIndex == 4)
        {
            if (_sharedBreadcrumbs.Count <= 1)
            {
                return;
            }

            _sharedBreadcrumbs.RemoveAt(_sharedBreadcrumbs.Count - 1);
            var previous = _sharedBreadcrumbs[^1];
            _sharedFolderId = previous.FolderId;
            _sharedFolderName = previous.Name;
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
        else if (SelectedSourceFilterIndex == 4)
        {
            var index = _sharedBreadcrumbs.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            while (_sharedBreadcrumbs.Count - 1 > index)
            {
                _sharedBreadcrumbs.RemoveAt(_sharedBreadcrumbs.Count - 1);
            }

            _sharedFolderId = item.FolderId;
            _sharedFolderName = item.Name;
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
        var active = SelectedSourceFilterIndex == 1
            ? _localBreadcrumbs
            : SelectedSourceFilterIndex == 4
                ? _sharedBreadcrumbs
                : _driveBreadcrumbs;
        Breadcrumbs.Clear();
        for (var i = 0; i < active.Count; i++)
        {
            var crumb = active[i];
            crumb.ShowSeparator = i < active.Count - 1;
            Breadcrumbs.Add(crumb);
        }

        if (SelectedSourceFilterIndex == 1)
        {
            CurrentFolderId = _localSyncFolderId ?? LocalRootCrumbId;
            CurrentFolderName = _localBreadcrumbs.LastOrDefault()?.Name ?? Loc.Text("FileHubFilterLocal");
        }
        else if (SelectedSourceFilterIndex == 4)
        {
            CurrentFolderId = _sharedFolderId;
            CurrentFolderName = _sharedFolderName;
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
            _isBatchUpdatingFiles = true;
            try
            {
                foreach (var factory in _allPageFactories.Skip(skip).Take(ItemsPerPage))
                {
                    Files.Add(factory());
                }
            }
            finally
            {
                _isBatchUpdatingFiles = false;
            }

            SyncGridSections();
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

    internal int MinimumRefreshVisualMs { get; set; } = 450;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await RefreshInternalAsync(MinimumRefreshVisualMs);
    }

    private async Task<bool> RefreshInternalAsync(int minVisualDurationMs = 0)
    {
        var visualStartMs = minVisualDurationMs > 0 ? Environment.TickCount64 : 0L;
        var filter = SelectedSourceFilterIndex;
        var hasNetwork = _driveClient.IsConnected && _syncService.IsOnline;
        var hasSavedFavorites = (_settingsService?.Current.FavoriteKeys.Count ?? 0) > 0;
        if (!hasNetwork && filter != 1 && !(filter == 3 && hasSavedFavorites))
        {
            ClearWhenDisconnected();
            StatusSummary = !_driveClient.IsConnected
                ? Loc.Text("ConnectBeforeSync")
                : Loc.Text("OfflineModeNotice");
            return false;
        }

        var refreshCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _refreshCts, refreshCts);
        if (previousCts is not null)
        {
            try
            {
                previousCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

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
            else if (filter == 4)
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

                IReadOnlyList<RemoteDriveNode> sharedNodes = string.Equals(_sharedFolderId, SharedRootCrumbId, StringComparison.Ordinal)
                    ? await _driveClient.ListSharedWithMeAsync(200, ct)
                    : await _driveClient.ListChildrenAsync(_sharedFolderId, foldersOnly: false, ct);

                foreach (var node in sharedNodes)
                {
                    if (node.Trashed)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(query) &&
                        !node.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
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
            else if (filter == 3)
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

                var addedRemoteIds = new HashSet<string>(StringComparer.Ordinal);
                var addedLocalPaths = new HashSet<string>(StringComparer.Ordinal);

                if (hasNetwork)
                {
                    var starredNodes = await _driveClient.ListStarredFilesAsync(200, ct) ?? [];
                    await SyncKnownStarredRemoteIdsAsync(starredNodes);

                    foreach (var node in starredNodes)
                    {
                        if (node.Trashed)
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(query) &&
                            !node.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var captured = node;
                        captured.Starred = true;
                        if (!string.IsNullOrWhiteSpace(captured.Id))
                        {
                            addedRemoteIds.Add(captured.Id);
                        }

                        if (captured.IsFolder)
                        {
                            mappedByRemoteId.TryGetValue(captured.Id, out var mappedFolder);
                            if (mappedFolder is not null)
                            {
                                addedLocalPaths.Add(NormalizeLocalFavoritePath(mappedFolder.LocalPath));
                            }

                            factories.Add(() => CreateRemoteFolderItem(captured, culture, mappedFolder));
                        }
                        else
                        {
                            if (trackedByRemoteId.TryGetValue(captured.Id, out var trackedFile) &&
                                syncFolders.TryGetValue(trackedFile.SyncFolderId, out var ownerFolder))
                            {
                                var diskPath = Path.Combine(
                                    ownerFolder.LocalPath,
                                    trackedFile.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                                addedLocalPaths.Add(NormalizeLocalFavoritePath(diskPath));
                            }

                            factories.Add(() => CreateRemoteFileItem(captured, trackedByRemoteId, syncFolders, culture));
                        }
                    }
                }
                else
                {
                    foreach (var entry in allTracked)
                    {
                        if (string.IsNullOrWhiteSpace(entry.RemoteFileId) ||
                            !_knownStarredRemoteIds.Contains(entry.RemoteFileId) ||
                            !syncFolders.TryGetValue(entry.SyncFolderId, out var syncFolder))
                        {
                            continue;
                        }

                        var diskPath = Path.Combine(
                            syncFolder.LocalPath,
                            entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                        var exists = entry.IsFolder ? Directory.Exists(diskPath) : File.Exists(diskPath);
                        if (!exists)
                        {
                            continue;
                        }

                        var name = Path.GetFileName(entry.RelativePath);
                        if (!string.IsNullOrWhiteSpace(query) &&
                            !name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        addedRemoteIds.Add(entry.RemoteFileId);
                        addedLocalPaths.Add(NormalizeLocalFavoritePath(diskPath));
                        var capturedEntry = entry;
                        factories.Add(() => CreateTrackedItem(capturedEntry, syncFolders, culture, true));
                    }
                }

                AddLocalFavoriteFactories(
                    factories,
                    syncFolders,
                    allTracked,
                    addedRemoteIds,
                    addedLocalPaths,
                    query,
                    culture);
            }
            else if (filter == 1)
            {
                if (hasNetwork && !_hasLoadedRemoteStarredIds)
                {
                    try
                    {
                        var starredNodes = await _driveClient.ListStarredFilesAsync(200, ct);
                        if (starredNodes is not null)
                        {
                            await SyncKnownStarredRemoteIdsAsync(starredNodes);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Starred state is non-critical when listing local synced items.
                    }
                }

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

                    if (!string.IsNullOrWhiteSpace(node.Id))
                    {
                        if (node.Starred)
                        {
                            _knownStarredRemoteIds.Add(node.Id);
                        }
                        else
                        {
                            _knownStarredRemoteIds.Remove(node.Id);
                        }
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

            lock (_refreshApplyLock)
            {
                if (ct.IsCancellationRequested || !ReferenceEquals(_refreshCts, refreshCts))
                {
                    return false;
                }

                _allPageFactories = factories;
                ApplyPage(1);
            }

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
                : filter == 4
                    ? (string.IsNullOrWhiteSpace(query)
                        ? Loc.Format("FileHubCountFmt", factories.Count, CurrentFolderName)
                        : Loc.Format("FileHubSearchCountFmt", factories.Count))
                : filter == 3
                    ? (!hasNetwork
                        ? $"{Loc.Format("FileHubCountFmt", factories.Count, Loc.Text("FileHubFilterFavorites"))} • {Loc.Text("OfflineModeNotice")}"
                        : string.IsNullOrWhiteSpace(query)
                            ? Loc.Format("FileHubCountFmt", factories.Count, Loc.Text("FileHubFilterFavorites"))
                            : Loc.Format("FileHubSearchCountFmt", factories.Count))
                    : string.IsNullOrWhiteSpace(query) && filter == 0
                        ? Loc.Format("FileHubCountFmt", factories.Count, CurrentFolderName)
                        : Loc.Format("FileHubSearchCountFmt", factories.Count);
            if (minVisualDurationMs > 0)
            {
                var elapsedMs = (int)(Environment.TickCount64 - visualStartMs);
                var remainingMs = minVisualDurationMs - elapsedMs;
                if (remainingMs > 0)
                {
                    await Task.Delay(remainingMs, ct);
                }
            }
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
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshCts, null, refreshCts), refreshCts))
            {
                IsLoading = false;
            }

            refreshCts.Dispose();
        }
    }

    private async Task SyncKnownStarredRemoteIdsAsync(IReadOnlyList<RemoteDriveNode> starredNodes)
    {
        _knownStarredRemoteIds.Clear();
        var activeRemoteKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in starredNodes)
        {
            if (!node.Trashed && !string.IsNullOrWhiteSpace(node.Id))
            {
                _knownStarredRemoteIds.Add(node.Id);
                activeRemoteKeys.Add(DriveFavoritePrefix + node.Id);
            }
        }

        _hasLoadedRemoteStarredIds = true;
        if (_settingsService is null)
        {
            return;
        }

        var keys = _settingsService.Current.FavoriteKeys;
        var changed = false;
        for (var i = keys.Count - 1; i >= 0; i--)
        {
            if (keys[i].StartsWith(DriveFavoritePrefix, StringComparison.Ordinal) &&
                !activeRemoteKeys.Contains(keys[i]))
            {
                keys.RemoveAt(i);
                changed = true;
            }
        }

        foreach (var key in activeRemoteKeys)
        {
            if (!keys.Contains(key, StringComparer.Ordinal))
            {
                keys.Add(key);
                changed = true;
            }
        }

        if (changed)
        {
            await _settingsService.SaveSilentlyAsync(_settingsService.Current);
        }
    }

    private void AddLocalFavoriteFactories(
        List<Func<FileHubItemViewModel>> factories,
        IReadOnlyDictionary<string, LocalSyncFolder> syncFolders,
        IReadOnlyList<TrackedFileEntry> allTracked,
        HashSet<string> addedRemoteIds,
        HashSet<string> addedLocalPaths,
        string query,
        CultureInfo culture)
    {
        if (_settingsService?.Current.FavoriteKeys is not { Count: > 0 } keys)
        {
            return;
        }

        var trackedByFullPath = new Dictionary<string, TrackedFileEntry>(StringComparer.Ordinal);
        foreach (var entry in allTracked)
        {
            if (syncFolders.TryGetValue(entry.SyncFolderId, out var owner))
            {
                var full = NormalizeLocalFavoritePath(Path.Combine(
                    owner.LocalPath,
                    entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                trackedByFullPath.TryAdd(full, entry);
            }
        }

        var folderByFullPath = syncFolders.Values
            .GroupBy(f => NormalizeLocalFavoritePath(f.LocalPath), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (!key.StartsWith(LocalFavoritePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var path = key[LocalFavoritePrefix.Length..];
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalized = NormalizeLocalFavoritePath(path);
            if (addedLocalPaths.Contains(normalized))
            {
                continue;
            }

            var name = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(query) &&
                !name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (folderByFullPath.TryGetValue(normalized, out var mappedSyncFolder) &&
                Directory.Exists(normalized))
            {
                if (!string.IsNullOrWhiteSpace(mappedSyncFolder.RemoteFolderId) &&
                    addedRemoteIds.Contains(mappedSyncFolder.RemoteFolderId))
                {
                    continue;
                }

                addedLocalPaths.Add(normalized);
                var capturedFolder = mappedSyncFolder;
                factories.Add(() => CreateLocalFolderItem(
                    capturedFolder.Id,
                    string.Empty,
                    GetSyncFolderDisplayName(capturedFolder),
                    capturedFolder.LocalPath,
                    capturedFolder.RemoteFolderId,
                    Directory.GetLastWriteTimeUtc(normalized),
                    culture,
                    capturedFolder));
                continue;
            }

            if (trackedByFullPath.TryGetValue(normalized, out var trackedEntry))
            {
                if (!string.IsNullOrWhiteSpace(trackedEntry.RemoteFileId) &&
                    addedRemoteIds.Contains(trackedEntry.RemoteFileId))
                {
                    continue;
                }

                var exists = trackedEntry.IsFolder ? Directory.Exists(normalized) : File.Exists(normalized);
                if (!exists)
                {
                    continue;
                }

                addedLocalPaths.Add(normalized);
                if (trackedEntry.IsFolder && syncFolders.TryGetValue(trackedEntry.SyncFolderId, out _))
                {
                    var capturedTracked = trackedEntry;
                    factories.Add(() => CreateLocalFolderItem(
                        capturedTracked.SyncFolderId,
                        capturedTracked.RelativePath.Replace('\\', '/').Trim('/'),
                        name,
                        normalized,
                        capturedTracked.RemoteFileId,
                        capturedTracked.LocalModifiedAtUtc,
                        culture));
                }
                else
                {
                    var capturedTracked = trackedEntry;
                    factories.Add(() => CreateTrackedItem(capturedTracked, syncFolders, culture, true));
                }
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
            ThumbnailLink = node.ThumbnailLink,
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

    private FolderAppearanceOverride? ResolveFolderAppearance(LocalSyncFolder? mappedFolder, string? appearanceKey)
    {
        if (mappedFolder is not null &&
            (!string.IsNullOrWhiteSpace(mappedFolder.CustomAlias) ||
             !string.IsNullOrWhiteSpace(mappedFolder.CustomColorHex) ||
             !string.IsNullOrWhiteSpace(mappedFolder.CustomIconKey)))
        {
            return new FolderAppearanceOverride(mappedFolder.CustomAlias, mappedFolder.CustomColorHex, mappedFolder.CustomIconKey);
        }

        if (!string.IsNullOrWhiteSpace(appearanceKey) &&
            _settingsService?.Current.FolderAppearances is { } dict &&
            dict.TryGetValue(appearanceKey, out var stored))
        {
            return stored;
        }

        return null;
    }

    private FileHubItemViewModel CreateRemoteFolderItem(
        RemoteDriveNode node,
        CultureInfo culture,
        LocalSyncFolder? mappedFolder = null)
    {
        var folderId = node.Id;
        var folderName = node.Name;
        var appearanceKey = $"drive:{folderId}";
        var appearance = ResolveFolderAppearance(mappedFolder, appearanceKey);

        var displayName = !string.IsNullOrWhiteSpace(appearance?.Alias)
            ? appearance!.Alias!.Trim()
            : node.Name;
        var iconData = FolderCustomizationPresets.ResolveIconSvg(appearance?.IconKey);
        var normalizedHex = FolderCustomizationPresets.NormalizeColorHex(appearance?.ColorHex);
        var iconBrush = !string.IsNullOrWhiteSpace(normalizedHex)
            ? SolidColorBrush.Parse(normalizedHex)
            : BrushMutedGray;

        FileHubItemViewModel? item = null;
        item = new FileHubItemViewModel
        {
            Name = displayName,
            OriginalFolderName = folderName,
            PathDisplay = Loc.Text("FileHubStatusFolder"),
            OriginDisplay = Loc.Text("FileHubStatusFolder"),
            OriginBrush = BrushFolderBlue,
            SizeDisplay = "—",
            ModifiedDisplay = node.ModifiedTimeUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", culture),
            MimeType = node.MimeType,
            IsFolder = true,
            IsStarred = node.Starred,
            HasLocalFile = false,
            RemoteFileId = folderId,
            SyncFolderId = mappedFolder?.Id,
            AppearanceKey = appearanceKey,
            CustomAlias = appearance?.Alias,
            CustomColorHex = normalizedHex,
            CustomIconKey = appearance?.IconKey,
            FileIconData = iconData,
            FileIconBrush = iconBrush,
            ActionLabel = Loc.Text("FileHubOpenFolder"),
            OpenInDriveLabel = Loc.Text("OpenInDriveButton"),
            TrashLabel = Loc.Text("MoveToTrashButton"),
            CopyNameLabel = Loc.Text("CopyNameMenu"),
            CopyLinkLabel = Loc.Text("CopyDriveLinkMenu"),
            AddToFavoritesLabel = Loc.Text("AddToFavoritesButton"),
            RemoveFromFavoritesLabel = Loc.Text("RemoveFromFavoritesButton"),
            OpenActionCommand = new AsyncRelayCommand(() => OpenRemoteFolderAsync(folderId, displayName)),
            OpenInDriveCommand = new AsyncRelayCommand(() => OpenRemoteInDriveBrowserAsync(folderId, true)),
            TrashCommand = new AsyncRelayCommand(() => MoveToTrashAsync(folderId, folderName)),
            CopyNameCommand = new AsyncRelayCommand(() => CopyToClipboardAsync(folderName)),
            CopyDriveLinkCommand = new AsyncRelayCommand(() => CopyToClipboardAsync($"https://drive.google.com/drive/folders/{folderId}")),
            ToggleFavoriteCommand = new AsyncRelayCommand(() => ToggleItemFavoriteAsync(item!))
        };
        return item;
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
        FileHubItemViewModel? item = null;
        item = new FileHubItemViewModel
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
            IsStarred = node.Starred,
            HasLocalFile = isSynced,
            LocalFullPath = resolvedLocalPath,
            RemoteFileId = nodeId,
            ThumbnailLink = node.ThumbnailLink,
            FileIconData = iconData,
            FileIconBrush = iconBrush,
            ActionLabel = isSynced ? Loc.Text("FileHubOpenLocal") : Loc.Text("FileHubOpenDrive"),
            DownloadLabel = Loc.Text("DownloadButton"),
            PreviewLabel = Loc.Text("PreviewButton"),
            OpenInDriveLabel = Loc.Text("OpenInDriveButton"),
            TrashLabel = Loc.Text("MoveToTrashButton"),
            CopyNameLabel = Loc.Text("CopyNameMenu"),
            CopyLinkLabel = Loc.Text("CopyDriveLinkMenu"),
            AddToFavoritesLabel = Loc.Text("AddToFavoritesButton"),
            RemoveFromFavoritesLabel = Loc.Text("RemoveFromFavoritesButton"),
            OpenActionCommand = new AsyncRelayCommand(() => OpenFileItemAsync(isSynced, resolvedLocalPath, nodeId)),
            DownloadCommand = new AsyncRelayCommand(() => DownloadFileAsync(nodeId, nodeName, node.SizeBytes, node.IsGoogleWorkspaceDoc, node.MimeType)),
            OpenInDriveCommand = new AsyncRelayCommand(() => OpenRemoteInDriveBrowserAsync(nodeId, false)),
            TrashCommand = new AsyncRelayCommand(() => MoveToTrashAsync(nodeId, nodeName)),
            CopyNameCommand = new AsyncRelayCommand(() => CopyToClipboardAsync(nodeName)),
            CopyDriveLinkCommand = new AsyncRelayCommand(() => CopyToClipboardAsync($"https://drive.google.com/file/d/{nodeId}/view")),
            ToggleFavoriteCommand = new AsyncRelayCommand(() => ToggleItemFavoriteAsync(item!))
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

        IsRenameOpen = false;
        IsMoveOpen = false;
        OpenCreateFolder();
    }

    [RelayCommand]
    private void OpenRename()
    {
        if (!CanRenameSelected || SelectedItem is null)
        {
            return;
        }

        IsCreateFolderOpen = false;
        IsMoveOpen = false;
        RenameNewName = !string.IsNullOrWhiteSpace(SelectedItem.OriginalFolderName)
            ? SelectedItem.OriginalFolderName
            : SelectedItem.Name;
        IsRenameOpen = true;
    }

    [RelayCommand]
    private void CancelRename() => IsRenameOpen = false;

    [RelayCommand]
    private async Task ConfirmRenameAsync()
    {
        if (!CanRenameSelected || SelectedItem is null)
        {
            IsRenameOpen = false;
            return;
        }

        var trimmed = (RenameNewName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed.Contains('/') ||
            trimmed.Contains('\\') ||
            trimmed is "." or "..")
        {
            return;
        }

        var item = SelectedItem;
        IsLoading = true;
        try
        {
            if (IsDriveFilter && !string.IsNullOrWhiteSpace(item.RemoteFileId) && _driveClient.IsConnected)
            {
                await _driveClient.MoveFileAsync(item.RemoteFileId, newName: trimmed);
            }
            else if (item.HasLocalFile && !string.IsNullOrWhiteSpace(item.LocalFullPath))
            {
                var parentDir = Path.GetDirectoryName(item.LocalFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(parentDir))
                {
                    return;
                }

                var destinationPath = Path.Combine(parentDir, trimmed);
                if (item.IsFolder && Directory.Exists(item.LocalFullPath))
                {
                    Directory.Move(item.LocalFullPath, destinationPath);
                }
                else if (File.Exists(item.LocalFullPath))
                {
                    File.Move(item.LocalFullPath, destinationPath, overwrite: false);
                }
            }

            IsRenameOpen = false;
            StatusSummary = Loc.Format("RenamedSuccessFmt", trimmed);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusSummary = Loc.Format("RenameErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenMove()
    {
        if (!CanMoveSelected)
        {
            return;
        }

        IsCreateFolderOpen = false;
        IsRenameOpen = false;

        var selectedSet = GetEffectiveSelectedItems().ToHashSet();
        MoveDestinations.Clear();

        if (IsDriveFilter)
        {
            if (!string.Equals(_driveFolderId, "root", StringComparison.Ordinal))
            {
                MoveDestinations.Add(new FileHubMoveDestinationItem("root", Loc.Text("MyDriveLabel")));
            }

            if (_driveBreadcrumbs.Count >= 2)
            {
                var parentCrumb = _driveBreadcrumbs[^2];
                if (!string.Equals(parentCrumb.FolderId, "root", StringComparison.Ordinal) &&
                    !string.Equals(parentCrumb.FolderId, _driveFolderId, StringComparison.Ordinal))
                {
                    MoveDestinations.Add(new FileHubMoveDestinationItem(parentCrumb.FolderId, $".. / {parentCrumb.Name}"));
                }
            }

            foreach (var folder in Files.Where(f => f.IsFolder && !selectedSet.Contains(f) && !string.IsNullOrWhiteSpace(f.RemoteFileId)))
            {
                MoveDestinations.Add(new FileHubMoveDestinationItem(folder.RemoteFileId, folder.Name));
            }
        }
        else if (IsLocalFilter)
        {
            foreach (var folder in Files.Where(f => f.IsFolder && !selectedSet.Contains(f) && !string.IsNullOrWhiteSpace(f.LocalFullPath)))
            {
                MoveDestinations.Add(new FileHubMoveDestinationItem(folder.LocalFullPath!, folder.Name));
            }
        }

        if (MoveDestinations.Count == 0)
        {
            return;
        }

        SelectedMoveDestinationIndex = 0;
        IsMoveOpen = true;
    }

    [RelayCommand]
    private void CancelMove() => IsMoveOpen = false;

    [RelayCommand]
    private async Task ConfirmMoveAsync()
    {
        if (!CanMoveSelected || SelectedMoveDestinationIndex < 0 || SelectedMoveDestinationIndex >= MoveDestinations.Count)
        {
            IsMoveOpen = false;
            return;
        }

        var destination = MoveDestinations[SelectedMoveDestinationIndex];
        var targets = GetEffectiveSelectedItems();
        if (targets.Count == 0)
        {
            IsMoveOpen = false;
            return;
        }

        IsLoading = true;
        try
        {
            var movedCount = 0;
            foreach (var item in targets)
            {
                if (IsDriveFilter && !string.IsNullOrWhiteSpace(item.RemoteFileId) && _driveClient.IsConnected)
                {
                    if (string.Equals(item.RemoteFileId, destination.TargetId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    await _driveClient.MoveFileAsync(item.RemoteFileId, destinationParentFolderId: destination.TargetId);
                    movedCount++;
                }
                else if (IsLocalFilter && item.HasLocalFile && !string.IsNullOrWhiteSpace(item.LocalFullPath) && Directory.Exists(destination.TargetId))
                {
                    var itemName = Path.GetFileName(item.LocalFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var targetPath = Path.Combine(destination.TargetId, itemName);
                    if (string.Equals(item.LocalFullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (item.IsFolder && Directory.Exists(item.LocalFullPath))
                    {
                        Directory.Move(item.LocalFullPath, targetPath);
                        movedCount++;
                    }
                    else if (File.Exists(item.LocalFullPath))
                    {
                        File.Move(item.LocalFullPath, targetPath, overwrite: false);
                        movedCount++;
                    }
                }
            }

            IsMoveOpen = false;
            StatusSummary = Loc.Format("MovedSuccessFmt", movedCount, destination.DisplayName);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusSummary = Loc.Format("MoveErrorFmt", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenCustomizeFolder()
    {
        if (SelectedItem is not { IsFolder: true, IsInTrash: false } folder)
        {
            return;
        }

        _customizeTargetSyncFolderId = folder.SyncFolderId;
        _customizeTargetAppearanceKey = folder.AppearanceKey;
        CustomizeTargetTitle = string.IsNullOrWhiteSpace(folder.OriginalFolderName) ? folder.Name : folder.OriginalFolderName;
        CustomizeEditAlias = folder.CustomAlias ?? string.Empty;
        CustomizeEditColorHex = FolderCustomizationPresets.NormalizeColorHex(folder.CustomColorHex)
            ?? FolderCustomizationPresets.DefaultColorHex;
        CustomizeEditIconKey = string.IsNullOrWhiteSpace(folder.CustomIconKey)
            ? FolderCustomizationPresets.DefaultIconKey
            : folder.CustomIconKey;
        RebuildCustomizeChoices();
        IsCreateFolderOpen = false;
        IsCustomizeFolderOpen = true;
    }

    [RelayCommand]
    private void CancelFolderCustomization()
    {
        IsCustomizeFolderOpen = false;
    }

    [RelayCommand]
    private async Task SaveFolderCustomizationAsync()
    {
        await ApplyFolderCustomizationAsync(
            CustomizeEditAlias,
            CustomizeEditColorHex,
            CustomizeEditIconKey);
    }

    [RelayCommand]
    private async Task ResetFolderCustomizationAsync()
    {
        CustomizeEditAlias = string.Empty;
        CustomizeEditColorHex = FolderCustomizationPresets.DefaultColorHex;
        CustomizeEditIconKey = FolderCustomizationPresets.DefaultIconKey;
        await ApplyFolderCustomizationAsync(null, null, null);
    }

    private async Task ApplyFolderCustomizationAsync(string? alias, string? colorHex, string? iconKey)
    {
        var normalizedAlias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        var normalizedColor = FolderCustomizationPresets.NormalizeColorHex(colorHex);
        var normalizedIcon = string.IsNullOrWhiteSpace(iconKey) ? null : iconKey.Trim();

        if (!string.IsNullOrWhiteSpace(_customizeTargetSyncFolderId))
        {
            await _syncService.UpdateFolderCustomizationAsync(
                _customizeTargetSyncFolderId,
                normalizedAlias,
                normalizedColor,
                normalizedIcon);
        }

        if (!string.IsNullOrWhiteSpace(_customizeTargetAppearanceKey) && _settingsService is not null)
        {
            var appearances = _settingsService.Current.FolderAppearances;
            if (normalizedAlias is null && normalizedColor is null && normalizedIcon is null)
            {
                appearances.Remove(_customizeTargetAppearanceKey);
            }
            else
            {
                appearances[_customizeTargetAppearanceKey] = new FolderAppearanceOverride(
                    normalizedAlias,
                    normalizedColor,
                    normalizedIcon);
            }

            await _settingsService.SaveSilentlyAsync(_settingsService.Current);
        }

        IsCustomizeFolderOpen = false;
        await RefreshAsync();
    }

    private void RebuildCustomizeChoices()
    {
        var activeBorder = ThemeAccentService.ResolveThemeBrush("#E2E2E9", "#1A1C20");
        var inactiveBorder = ThemeAccentService.ResolveThemeBrush("#44474F", "#C4C6D0");
        var activeIconBorder = ThemeAccentService.ResolveThemeBrush("#A8C7FA", "#0B57D0");

        CustomizeColorChoices.Clear();
        foreach (var preset in FolderCustomizationPresets.Colors)
        {
            var hex = preset.Hex;
            var isSelected = string.Equals(CustomizeEditColorHex, hex, StringComparison.OrdinalIgnoreCase);
            CustomizeColorChoices.Add(new FolderColorChoice(
                hex,
                SolidColorBrush.Parse(hex),
                isSelected,
                isSelected ? activeBorder : inactiveBorder,
                new RelayCommand(() =>
                {
                    CustomizeEditColorHex = hex;
                    RebuildCustomizeChoices();
                })));
        }

        var isEnglish = string.Equals(Loc.CurrentLanguage, "en-US", StringComparison.OrdinalIgnoreCase);
        CustomizeIconChoices.Clear();
        foreach (var icon in FolderCustomizationPresets.Icons)
        {
            var key = icon.Key;
            var isSelected = string.Equals(CustomizeEditIconKey, key, StringComparison.OrdinalIgnoreCase);
            CustomizeIconChoices.Add(new FolderIconChoice(
                key,
                TryParseIconGeometry(icon.SvgData),
                isEnglish ? icon.NameEn : icon.NamePt,
                isSelected,
                isSelected ? activeIconBorder : inactiveBorder,
                new RelayCommand(() =>
                {
                    CustomizeEditIconKey = key;
                    RebuildCustomizeChoices();
                })));
        }
    }

    private static Geometry? TryParseIconGeometry(string svgData)
    {
        try
        {
            return Geometry.Parse(svgData);
        }
        catch
        {
            return null;
        }
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

    private int? GetEffectiveTransferConcurrencyLimit() =>
        _settingsService is not null
            ? _settingsService.Current.GetEffectiveTransferConcurrencyLimit()
            : 3;

    public async Task UploadDroppedFilesAsync(IEnumerable<string> localPaths)
    {
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

        TransferErrorBannerText = string.Empty;
        var rootTargetFolder = SelectedSourceFilterIndex == 0 ? CurrentFolderId : "root";

        IsLoading = true;
        try
        {
            var (jobs, createdFolders) = await CollectUploadJobsAsync(localPaths, rootTargetFolder);
            if (jobs.Count == 0)
            {
                if (createdFolders > 0)
                {
                    await RefreshAsync();
                }
                return;
            }

            var total = jobs.Count;
            var completed = 0;
            var uploadedCount = 0;
            var concurrencyLimit = GetEffectiveTransferConcurrencyLimit();
            using var gate = concurrencyLimit is int limit ? new SemaphoreSlim(limit, limit) : null;

            var tasks = jobs.Select(async job =>
            {
                if (gate is not null)
                {
                    await gate.WaitAsync();
                }

                var fileName = Path.GetFileName(job.LocalPath);
                try
                {
                    var current = Interlocked.Increment(ref completed);
                    Dispatcher.UIThread.Post(() =>
                        StatusSummary = total > 1
                            ? $"({current}/{total}) {Loc.Format("FileHubUploadingFmt", fileName)}"
                            : Loc.Format("FileHubUploadingFmt", fileName));

                    await _syncService.UploadSingleFileAsync(job.LocalPath, job.RemoteFolderId);
                    Interlocked.Increment(ref uploadedCount);
                }
                catch (Exception ex)
                {
                    var err = Loc.Format("UploadErrorBannerFmt", fileName, ex.Message);
                    Dispatcher.UIThread.Post(() =>
                    {
                        TransferErrorBannerText = err;
                        StatusSummary = err;
                    });
                }
                finally
                {
                    gate?.Release();
                }
            });

            await Task.WhenAll(tasks);

            if (uploadedCount > 0)
            {
                StatusSummary = uploadedCount == 1
                    ? Loc.Format("FileHubUploadedFmt", Path.GetFileName(jobs[0].LocalPath))
                    : Loc.Format("FilesUploadedBatchFmt", uploadedCount);
                await RefreshAsync();
            }
            else if (createdFolders > 0)
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            var err = Loc.Format("FileHubErrorFmt", ex.Message);
            TransferErrorBannerText = err;
            StatusSummary = err;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<(List<(string LocalPath, string RemoteFolderId)> Jobs, int CreatedFolders)> CollectUploadJobsAsync(
        IEnumerable<string> localPaths,
        string rootTargetFolderId)
    {
        var jobs = new List<(string LocalPath, string RemoteFolderId)>();
        var createdFolders = 0;

        async Task CollectDirectoryAsync(string directoryPath, string parentRemoteFolderId)
        {
            var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(directoryPath));
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return;
            }

            var remoteDir = await _driveClient.FindOrCreateFolderAsync(folderName, parentRemoteFolderId);
            createdFolders++;
            foreach (var file in Directory.EnumerateFiles(directoryPath))
            {
                jobs.Add((file, remoteDir.Id));
            }

            foreach (var subDir in Directory.EnumerateDirectories(directoryPath))
            {
                await CollectDirectoryAsync(subDir, remoteDir.Id);
            }
        }

        foreach (var path in localPaths)
        {
            if (File.Exists(path))
            {
                jobs.Add((path, rootTargetFolderId));
            }
            else if (Directory.Exists(path))
            {
                await CollectDirectoryAsync(path, rootTargetFolderId);
            }
        }

        return (jobs, createdFolders);
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

    private Task LoadPreviewAsync(FileHubItemViewModel item, int pdfPage)
    {
        if (IsPreviewLoading &&
            IsPreviewVisible &&
            ReferenceEquals(_previewedItem, item) &&
            CurrentPdfPage == pdfPage &&
            _inFlightPreviewTask is { IsCompleted: false } existingTask)
        {
            return existingTask;
        }

        var task = LoadPreviewCoreAsync(item, pdfPage);
        _inFlightPreviewTask = task;
        return task;
    }

    private async Task LoadPreviewCoreAsync(FileHubItemViewModel item, int pdfPage)
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
            var instantThumb = await _previewService.TryGetCachedThumbnailBitmapAsync(
                item.RemoteFileId,
                item.LocalFullPath,
                item.Name,
                ct);
            if (instantThumb is not null && !ct.IsCancellationRequested)
            {
                CurrentPreviewKind = PreviewKind.Image;
                ReplacePreviewBitmap(instantThumb);
                PreviewMessage = string.Empty;
            }

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

            PreviewMessage = result.Kind == PreviewKind.Unsupported ||
                (result.Kind is PreviewKind.Video or PreviewKind.Audio && result.VisualBitmap is null)
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
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(6, 6);
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
                    ct,
                    item.ThumbnailLink);

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
        var effectiveRemoteId = isConnected ? remoteId : string.Empty;
        var isStarred = IsItemStarred(remoteId, fullLocalPath);
        FileHubItemViewModel? item = null;
        item = new FileHubItemViewModel
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
            IsStarred = isStarred,
            HasLocalFile = existsLocally,
            LocalFullPath = fullLocalPath,
            RemoteFileId = effectiveRemoteId,
            FileIconData = iconData,
            FileIconBrush = iconBrush,
            ActionLabel = existsLocally ? Loc.Text("FileHubOpenLocal") : Loc.Text("FileHubOpenDrive"),
            DownloadLabel = Loc.Text("DownloadButton"),
            PreviewLabel = Loc.Text("PreviewButton"),
            OpenInDriveLabel = Loc.Text("OpenInDriveButton"),
            TrashLabel = Loc.Text("MoveToTrashButton"),
            CopyNameLabel = Loc.Text("CopyNameMenu"),
            CopyLinkLabel = Loc.Text("CopyDriveLinkMenu"),
            AddToFavoritesLabel = Loc.Text("AddToFavoritesButton"),
            RemoveFromFavoritesLabel = Loc.Text("RemoveFromFavoritesButton"),
            OpenActionCommand = new AsyncRelayCommand(() => OpenFileItemAsync(existsLocally, fullLocalPath, effectiveRemoteId)),
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
                : null,
            ToggleFavoriteCommand = new AsyncRelayCommand(() => ToggleItemFavoriteAsync(item!))
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
        var appearanceKey = !string.IsNullOrWhiteSpace(remoteFolderId)
            ? $"drive:{remoteFolderId}"
            : $"local:{Path.GetFullPath(localFullPath)}";
        var appearance = ResolveFolderAppearance(customizedFolder, appearanceKey);

        var shownName = !string.IsNullOrWhiteSpace(appearance?.Alias)
            ? appearance!.Alias!.Trim()
            : displayName;
        var iconData = FolderCustomizationPresets.ResolveIconSvg(appearance?.IconKey);
        var normalizedHex = FolderCustomizationPresets.NormalizeColorHex(appearance?.ColorHex);
        var iconBrush = !string.IsNullOrWhiteSpace(normalizedHex)
            ? SolidColorBrush.Parse(normalizedHex)
            : BrushMutedGray;

        var isStarred = IsItemStarred(remoteFolderId, localFullPath);
        FileHubItemViewModel? item = null;
        item = new FileHubItemViewModel
        {
            Name = shownName,
            OriginalFolderName = displayName,
            PathDisplay = localFullPath,
            OriginDisplay = Loc.Text("FileHubStatusFolder"),
            OriginBrush = BrushFolderBlue,
            SizeDisplay = "—",
            ModifiedDisplay = modifiedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", culture),
            IsFolder = true,
            IsStarred = isStarred,
            HasLocalFile = Directory.Exists(localFullPath),
            LocalFullPath = localFullPath,
            RemoteFileId = hasRemote ? remoteFolderId : string.Empty,
            SyncFolderId = customizedFolder?.Id,
            AppearanceKey = appearanceKey,
            CustomAlias = appearance?.Alias,
            CustomColorHex = normalizedHex,
            CustomIconKey = appearance?.IconKey,
            FileIconData = iconData,
            FileIconBrush = iconBrush,
            ActionLabel = Loc.Text("FileHubOpenFolder"),
            OpenInDriveLabel = Loc.Text("OpenInDriveButton"),
            CopyNameLabel = Loc.Text("CopyNameMenu"),
            CopyLinkLabel = Loc.Text("CopyDriveLinkMenu"),
            AddToFavoritesLabel = Loc.Text("AddToFavoritesButton"),
            RemoveFromFavoritesLabel = Loc.Text("RemoveFromFavoritesButton"),
            OpenActionCommand = new AsyncRelayCommand(() => OpenLocalFolderAsync(syncFolderId, subPath, shownName)),
            OpenInDriveCommand = hasRemote
                ? new AsyncRelayCommand(() => OpenRemoteInDriveBrowserAsync(remoteFolderId, true))
                : null,
            CopyNameCommand = new AsyncRelayCommand(() => CopyToClipboardAsync(displayName)),
            CopyDriveLinkCommand = hasRemote
                ? new AsyncRelayCommand(() => CopyToClipboardAsync($"https://drive.google.com/drive/folders/{remoteFolderId}"))
                : null,
            ToggleFavoriteCommand = new AsyncRelayCommand(() => ToggleItemFavoriteAsync(item!))
        };
        return item;
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
            string.IsNullOrWhiteSpace(folderId))
        {
            return;
        }

        if (SelectedSourceFilterIndex == 4)
        {
            if (string.Equals(_sharedFolderId, folderId, StringComparison.Ordinal))
            {
                return;
            }

            var prevSharedId = _sharedFolderId;
            var prevSharedName = _sharedFolderName;
            var prevSharedCount = _sharedBreadcrumbs.Count;

            SearchText = string.Empty;
            if (!string.Equals(_sharedBreadcrumbs.LastOrDefault()?.FolderId, folderId, StringComparison.Ordinal))
            {
                _sharedBreadcrumbs.Add(new FileHubBreadcrumbItem(folderId, folderName, NavigateToBreadcrumbAsync));
            }

            _sharedFolderId = folderId;
            _sharedFolderName = folderName;
            SyncBreadcrumbsFromActiveMode();

            var sharedOk = await RefreshInternalAsync();
            if (!sharedOk)
            {
                while (_sharedBreadcrumbs.Count > prevSharedCount)
                {
                    _sharedBreadcrumbs.RemoveAt(_sharedBreadcrumbs.Count - 1);
                }

                _sharedFolderId = prevSharedId;
                _sharedFolderName = prevSharedName;
                SyncBreadcrumbsFromActiveMode();
            }

            return;
        }

        var switchingToDrive = SelectedSourceFilterIndex != 0;
        if (!switchingToDrive && string.Equals(_driveFolderId, folderId, StringComparison.Ordinal))
        {
            return;
        }

        var prevFolderId = _driveFolderId;
        var prevFolderName = _driveFolderName;
        var prevCount = _driveBreadcrumbs.Count;

        SearchText = string.Empty;
        if (switchingToDrive)
        {
            ResetDriveNavigation();
            if (!string.Equals(folderId, "root", StringComparison.Ordinal))
            {
                _driveBreadcrumbs.Add(new FileHubBreadcrumbItem(folderId, folderName, NavigateToBreadcrumbAsync));
            }
        }
        else if (!string.Equals(_driveBreadcrumbs.LastOrDefault()?.FolderId, folderId, StringComparison.Ordinal))
        {
            _driveBreadcrumbs.Add(new FileHubBreadcrumbItem(folderId, folderName, NavigateToBreadcrumbAsync));
        }

        _driveFolderId = folderId;
        _driveFolderName = folderName;
        if (switchingToDrive)
        {
            _suppressNextFilterRefresh = true;
            try
            {
                SelectedSourceFilterIndex = 0;
            }
            finally
            {
                _suppressNextFilterRefresh = false;
            }
        }

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
        SourceFilterOptions.Add(Loc.Text("FileHubFilterFavorites"));
        SourceFilterOptions.Add(Loc.Text("FileHubFilterShared"));
        SelectedSourceFilterIndex = Math.Clamp(savedIndex, 0, SourceFilterOptions.Count - 1);
    }
}
