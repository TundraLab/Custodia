using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Converters;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;

namespace TundraDrive.Desktop.ViewModels;

public sealed record FolderColorChoice(
    string Hex,
    IBrush Brush,
    bool IsSelected,
    IBrush SelectionBorderBrush,
    IRelayCommand SelectCommand);

public sealed record FolderIconChoice(
    string Key,
    Geometry? Geometry,
    string Label,
    bool IsSelected,
    IBrush SelectionBorderBrush,
    IRelayCommand SelectCommand);

public sealed partial class SyncFolderItemViewModel : ViewModelBase
{
    private readonly SyncFoldersViewModel _parent;

    public ILocalizationService Loc { get; }
    public string Id { get; }

    [ObservableProperty]
    private bool _isCustomizing;

    [ObservableProperty]
    private string _editAlias = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditColorPreviewBrush))]
    private string _editColorHex = FolderCustomizationPresets.DefaultColorHex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditIconPreviewGeometry))]
    private string _editIconKey = FolderCustomizationPresets.DefaultIconKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomAlias))]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string? _customAlias;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomColorBrush))]
    private string? _customColorHex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomIconGeometry))]
    private string? _customIconKey;

    public ObservableCollection<FolderColorChoice> ColorChoices { get; } = [];
    public ObservableCollection<FolderIconChoice> IconChoices { get; } = [];
    public string LocalPath { get; }
    public string RemoteFolderId { get; }
    public string RemoteFolderName { get; }

    [ObservableProperty]
    private SyncMode _mode;

    [ObservableProperty]
    private FolderSyncState _state;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private DateTimeOffset? _lastSyncedAtUtc;

    [ObservableProperty]
    private string? _lastErrorMessage;

    [ObservableProperty]
    private int _trackedFileCount;

    public SyncFolderItemViewModel(
        LocalSyncFolder model,
        SyncFoldersViewModel parent,
        ILocalizationService localization)
    {
        _parent = parent;
        Loc = localization;
        Id = model.Id;
        LocalPath = model.LocalPath;
        RemoteFolderId = model.RemoteFolderId;
        RemoteFolderName = model.RemoteFolderName;
        _mode = model.Mode;
        _state = model.State;
        _isPaused = model.IsPaused;
        _lastSyncedAtUtc = model.LastSyncedAtUtc;
        _lastErrorMessage = model.LastErrorMessage;
        _trackedFileCount = model.TrackedFileCount;
        _customAlias = model.CustomAlias;
        _customColorHex = model.CustomColorHex;
        _customIconKey = model.CustomIconKey;
        _editAlias = model.CustomAlias ?? string.Empty;
        _editColorHex = FolderCustomizationPresets.NormalizeColorHex(model.CustomColorHex) ?? FolderCustomizationPresets.DefaultColorHex;
        _editIconKey = string.IsNullOrWhiteSpace(model.CustomIconKey) ? FolderCustomizationPresets.DefaultIconKey : model.CustomIconKey;
        RebuildCustomizationOptions();
    }

    public void UpdateFrom(LocalSyncFolder model)
    {
        Mode = model.Mode;
        State = model.State;
        IsPaused = model.IsPaused;
        LastSyncedAtUtc = model.LastSyncedAtUtc;
        LastErrorMessage = model.LastErrorMessage;
        TrackedFileCount = model.TrackedFileCount;
        CustomAlias = model.CustomAlias;
        CustomColorHex = model.CustomColorHex;
        CustomIconKey = model.CustomIconKey;
        if (!IsCustomizing)
        {
            EditAlias = model.CustomAlias ?? string.Empty;
            EditColorHex = FolderCustomizationPresets.NormalizeColorHex(model.CustomColorHex) ?? FolderCustomizationPresets.DefaultColorHex;
            EditIconKey = string.IsNullOrWhiteSpace(model.CustomIconKey) ? FolderCustomizationPresets.DefaultIconKey : model.CustomIconKey;
            RebuildCustomizationOptions();
        }
        RefreshLocalizedProperties();
        OnPropertyChanged(nameof(IsSyncing));
        OnPropertyChanged(nameof(IsErrorState));
        OnPropertyChanged(nameof(IsActiveMonitoring));
        OnPropertyChanged(nameof(IsTwoWayMode));
        OnPropertyChanged(nameof(AccentBorderBrush));
    }

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(IsTwoWayMode));
        OnPropertyChanged(nameof(DirectionBadgeText));
        OnPropertyChanged(nameof(DirectionExplanation));
        OnPropertyChanged(nameof(RemoteDestinationDisplay));
        OnPropertyChanged(nameof(SyncModeDisplay));
        OnPropertyChanged(nameof(ModeToggleButtonLabel));
        OnPropertyChanged(nameof(PauseButtonLabel));
        OnPropertyChanged(nameof(LastSyncedDisplay));
        OnPropertyChanged(nameof(StateDisplay));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(CustomColorBrush));
        OnPropertyChanged(nameof(CustomIconGeometry));
        RebuildCustomizationOptions();
    }

    public bool HasCustomAlias => !string.IsNullOrWhiteSpace(CustomAlias);

    public string DisplayTitle => !string.IsNullOrWhiteSpace(CustomAlias)
        ? CustomAlias!
        : Path.GetFileName(LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public IBrush CustomColorBrush =>
        SolidColorBrush.Parse(FolderCustomizationPresets.NormalizeColorHex(CustomColorHex) ?? FolderCustomizationPresets.DefaultColorHex);

    public Geometry CustomIconGeometry =>
        Geometry.Parse(FolderCustomizationPresets.ResolveIconSvg(CustomIconKey));

    public IBrush EditColorPreviewBrush =>
        SolidColorBrush.Parse(FolderCustomizationPresets.NormalizeColorHex(EditColorHex) ?? FolderCustomizationPresets.DefaultColorHex);

    public Geometry EditIconPreviewGeometry =>
        Geometry.Parse(FolderCustomizationPresets.ResolveIconSvg(EditIconKey));

    private void RebuildCustomizationOptions()
    {
        var choiceSelected = ThemeAccentService.ResolveThemeBrush("#E2E2E9", "#1A1C20");
        var choiceDefault = ThemeAccentService.ResolveThemeBrush("#44474F", "#C4C6D0");
        var isEn = string.Equals(Loc.CurrentLanguage, "en-US", StringComparison.OrdinalIgnoreCase);

        ColorChoices.Clear();
        foreach (var preset in FolderCustomizationPresets.Colors)
        {
            var hex = preset.Hex;
            var isSelected = string.Equals(EditColorHex, hex, StringComparison.OrdinalIgnoreCase);
            ColorChoices.Add(new FolderColorChoice(
                hex,
                SolidColorBrush.Parse(hex),
                isSelected,
                isSelected ? choiceSelected : choiceDefault,
                new RelayCommand(() =>
                {
                    EditColorHex = hex;
                    RebuildCustomizationOptions();
                })));
        }

        IconChoices.Clear();
        foreach (var icon in FolderCustomizationPresets.Icons)
        {
            var key = icon.Key;
            var isSelected = string.Equals(EditIconKey, key, StringComparison.OrdinalIgnoreCase);
            IconChoices.Add(new FolderIconChoice(
                key,
                Geometry.Parse(icon.SvgData),
                isEn ? icon.NameEn : icon.NamePt,
                isSelected,
                isSelected ? choiceSelected : choiceDefault,
                new RelayCommand(() =>
                {
                    EditIconKey = key;
                    RebuildCustomizationOptions();
                })));
        }
    }

    public bool IsSyncing => !IsPaused && State == FolderSyncState.Syncing;
    public bool IsErrorState => !IsPaused && State == FolderSyncState.Error;
    public bool IsActiveMonitoring => !IsPaused && State == FolderSyncState.Idle;
    public bool IsTwoWayMode => Mode == SyncMode.TwoWay;

    public string DirectionBadgeText => IsTwoWayMode
        ? Loc.Text("DirectionBadgeTwoWay")
        : Loc.Text("DirectionBadgeOneWay");

    public string DirectionExplanation => IsTwoWayMode
        ? Loc.Text("DirectionExplainTwoWay")
        : Loc.Text("DirectionExplainOneWay");

    public string RemoteDestinationDisplay => Loc.Format("RemoteDestFmt", RemoteFolderName);

    public string SyncModeDisplay =>
        Mode == SyncMode.TwoWay ? Loc.Text("ModeTwoWay") : Loc.Text("ModeOneWay");

    public string ModeToggleButtonLabel =>
        Mode == SyncMode.TwoWay ? Loc.Text("SwitchToBackup") : Loc.Text("SwitchToTwoWay");

    public string PauseButtonLabel => IsPaused ? Loc.Text("Resume") : Loc.Text("Pause");

    public string LastSyncedDisplay =>
        Loc.Format("LastSyncedFmt", Loc.FormatRelativeTimestamp(LastSyncedAtUtc), TrackedFileCount);

    public string StateDisplay => State switch
    {
        FolderSyncState.Syncing => Loc.Text("StateSyncing"),
        FolderSyncState.Paused => Loc.Text("StatePaused"),
        FolderSyncState.Error => string.IsNullOrWhiteSpace(LastErrorMessage)
            ? Loc.Text("StateErrorDefault")
            : Loc.Format("StateErrorFmt", LastErrorMessage),
        _ => Loc.Text("StateIdle")
    };

    public IBrush AccentBorderBrush => State switch
    {
        FolderSyncState.Syncing => ThemeAccentService.ResolveThemeBrush("#A8C7FA", "#0B57D0"),
        FolderSyncState.Paused => ThemeAccentService.ResolveThemeBrush("#8E9099", "#74777F"),
        FolderSyncState.Error => ThemeAccentService.ResolveThemeBrush("#F28B82", "#B3261E"),
        _ => ThemeAccentService.ResolveThemeBrush("#6DD58C", "#146C2E")
    };

    public void RefreshThemeBrushes()
    {
        OnPropertyChanged(nameof(AccentBorderBrush));
        if (IsCustomizing)
        {
            RebuildCustomizationOptions();
        }
    }

    [RelayCommand]
    private Task SyncNowAsync() => _parent.SyncSingleFolderAsync(Id);

    [RelayCommand]
    private Task TogglePauseAsync() => _parent.TogglePauseFolderAsync(Id, !IsPaused);

    [RelayCommand]
    private Task ToggleModeAsync()
    {
        var nextMode = Mode == SyncMode.TwoWay ? SyncMode.OneWayBackup : SyncMode.TwoWay;
        return _parent.ChangeFolderModeAsync(Id, nextMode);
    }

    [RelayCommand]
    private Task OpenLocalFolderAsync() => _parent.OpenLocalFolderAsync(LocalPath);

    [RelayCommand]
    private void ToggleCustomize()
    {
        IsCustomizing = !IsCustomizing;
        if (IsCustomizing)
        {
            EditAlias = CustomAlias ?? string.Empty;
            EditColorHex = FolderCustomizationPresets.NormalizeColorHex(CustomColorHex) ?? FolderCustomizationPresets.DefaultColorHex;
            EditIconKey = string.IsNullOrWhiteSpace(CustomIconKey) ? FolderCustomizationPresets.DefaultIconKey : CustomIconKey;
            RebuildCustomizationOptions();
        }
    }

    [RelayCommand]
    private async Task SaveCustomizationAsync()
    {
        await _parent.SaveFolderCustomizationAsync(Id, EditAlias, EditColorHex, EditIconKey);
        IsCustomizing = false;
    }

    [RelayCommand]
    private async Task ResetCustomizationAsync()
    {
        EditAlias = string.Empty;
        EditColorHex = FolderCustomizationPresets.DefaultColorHex;
        EditIconKey = FolderCustomizationPresets.DefaultIconKey;
        await _parent.SaveFolderCustomizationAsync(Id, null, null, null);
        IsCustomizing = false;
    }

    [RelayCommand]
    private void Remove() => _parent.RequestRemoveFolder(Id);
}

public sealed partial class SyncFoldersViewModel : ViewModelBase
{
    private const string GoogleOneStorageUrl = "https://one.google.com/storage";

    private readonly IDriveSyncService _syncService;
    private readonly IGoogleDriveClient _driveClient;
    private DriveAccount _lastAccount = new();
    private double _storageTrackWidth = 320.0;

    public ILocalizationService Loc { get; }

    [ObservableProperty]
    private bool _isRemoteBrowserActive;

    [ObservableProperty]
    private bool _isAccountConnected;

    [ObservableProperty]
    private double _quotaPercentage;

    [ObservableProperty]
    private string _quotaSummaryText = string.Empty;

    [ObservableProperty]
    private bool _hasStorageAlert;

    [ObservableProperty]
    private string _storageAlertMessage = string.Empty;

    [ObservableProperty]
    private IBrush _storageProgressBrush = ThemeAccentService.ResolveThemeBrush("#A8C7FA", "#0B57D0");

    [ObservableProperty]
    private IBrush _storageAlertBorderBrush = ThemeAccentService.ResolveThemeBrush("#44474F", "#C4C6D0");

    [ObservableProperty]
    private IBrush _storageAlertTextBrush = ThemeAccentService.ResolveThemeBrush("#F5C26B", "#9A5B00");

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasNoFolders = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFolder))]
    [NotifyPropertyChangedFor(nameof(SelectedPauseLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedModeToggleLabel))]
    private SyncFolderItemViewModel? _selectedFolder;

    [ObservableProperty]
    private double _driveBarWidth;

    [ObservableProperty]
    private double _otherBarWidth;

    [ObservableProperty]
    private double _trashBarWidth;

    [ObservableProperty]
    private string _driveLegendText = string.Empty;

    [ObservableProperty]
    private string _otherLegendText = string.Empty;

    [ObservableProperty]
    private string _trashLegendText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoveConfirmVisible))]
    [NotifyPropertyChangedFor(nameof(RemoveConfirmPromptText))]
    private string? _pendingRemoveFolderId;

    public bool IsRemoveConfirmVisible => !string.IsNullOrWhiteSpace(PendingRemoveFolderId);
    public string RemoveConfirmPromptText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PendingRemoveFolderId))
            {
                return string.Empty;
            }

            var folder = Folders.FirstOrDefault(f => string.Equals(f.Id, PendingRemoveFolderId, StringComparison.Ordinal));
            return Loc.Format("RemoveFolderConfirmFmt", folder?.DisplayTitle ?? string.Empty);
        }
    }

    public bool HasSelectedFolder => SelectedFolder is not null;
    public string SelectedPauseLabel => SelectedFolder?.PauseButtonLabel ?? Loc.Text("Pause");
    public string SelectedModeToggleLabel => SelectedFolder?.ModeToggleButtonLabel ?? Loc.Text("SwitchToBackup");

    public ObservableCollection<SyncFolderItemViewModel> Folders { get; } = [];

    public RemoteBrowserViewModel RemoteBrowser { get; }

    public Func<Task<string?>>? PickLocalFolderCallback { get; set; }
    public Func<Task<IReadOnlyList<string>>>? PickLocalFoldersCallback { get; set; }
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Func<string, Task>? OpenLocalFolderCallback { get; set; }

    public SyncFoldersViewModel(
        IDriveSyncService syncService,
        IGoogleDriveClient driveClient,
        RemoteBrowserViewModel remoteBrowser,
        ILocalizationService localization)
    {
        _syncService = syncService;
        _driveClient = driveClient;
        RemoteBrowser = remoteBrowser;
        Loc = localization;
        RemoteBrowser.RemoteFolderSelectedForSync += OnRemoteFolderSelectedAsync;
        RemoteBrowser.RemoteParentSelectedForAutoSubfolderSync += OnRemoteParentSelectedForAutoSubfolderSyncAsync;

        Loc.LanguageChanged += (_, _) =>
        {
            ApplyAccount(_lastAccount);
            foreach (var folder in Folders)
            {
                folder.RefreshLocalizedProperties();
            }
            OnPropertyChanged(nameof(SelectedPauseLabel));
            OnPropertyChanged(nameof(SelectedModeToggleLabel));
        };

        ThemeAccentService.ThemeChanged += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ApplyAccount(_lastAccount);
                foreach (var folder in Folders)
                {
                    folder.RefreshThemeBrushes();
                }
            });
        };

        ApplyAccount(_syncService.CurrentAccount);
    }

    public async Task RefreshFoldersAsync()
    {
        var list = await _syncService.GetSyncFoldersAsync();
        var incomingById = list.ToDictionary(f => f.Id, f => f, StringComparer.Ordinal);

        for (var i = Folders.Count - 1; i >= 0; i--)
        {
            if (!incomingById.ContainsKey(Folders[i].Id))
            {
                Folders.RemoveAt(i);
            }
        }

        foreach (var model in list)
        {
            var existing = Folders.FirstOrDefault(f => f.Id == model.Id);
            if (existing is not null)
            {
                existing.UpdateFrom(model);
            }
            else
            {
                Folders.Add(new SyncFolderItemViewModel(model, this, Loc));
            }
        }

        HasNoFolders = Folders.Count == 0;
        OnPropertyChanged(nameof(SelectedPauseLabel));
        OnPropertyChanged(nameof(SelectedModeToggleLabel));
    }

    public void ApplyAccount(DriveAccount account)
    {
        _lastAccount = account;
        IsAccountConnected = account.IsConnected;
        QuotaPercentage = account.Quota.UsagePercentage;
        var limit = account.Quota.LimitBytes > 0
            ? UiConverters.FormatBytes(account.Quota.LimitBytes)
            : Loc.Text("Unlimited");
        QuotaSummaryText = account.IsConnected
            ? Loc.Format("QuotaSummaryFmt", UiConverters.FormatBytes(account.Quota.UsageBytes), limit)
            : Loc.Text("AccountDisconnected");

        var isFull = account.IsConnected && account.Quota.LimitBytes > 0 &&
            (account.Quota.UsageBytes >= account.Quota.LimitBytes || account.Quota.UsagePercentage >= 99.5);
        HasStorageAlert = account.IsConnected && account.Quota.LimitBytes > 0 && account.Quota.UsagePercentage >= 80.0;
        StorageAlertMessage = isFull
            ? Loc.Format("StorageFullFmt", account.Quota.UsagePercentage)
            : HasStorageAlert
                ? Loc.Format("StorageWarningFmt", account.Quota.UsagePercentage)
                : string.Empty;
        StorageProgressBrush = isFull
            ? ThemeAccentService.ResolveThemeBrush("#F28B82", "#B3261E")
            : HasStorageAlert
                ? ThemeAccentService.ResolveThemeBrush("#F5C26B", "#9A5B00")
                : ThemeAccentService.ResolveThemeBrush("#A8C7FA", "#0B57D0");
        StorageAlertBorderBrush = isFull
            ? ThemeAccentService.ResolveThemeBrush("#F28B82", "#B3261E")
            : HasStorageAlert
                ? ThemeAccentService.ResolveThemeBrush("#F5C26B", "#9A5B00")
                : ThemeAccentService.ResolveThemeBrush("#44474F", "#C4C6D0");
        StorageAlertTextBrush = isFull
            ? ThemeAccentService.ResolveThemeBrush("#FFB4AB", "#BA1A1A")
            : ThemeAccentService.ResolveThemeBrush("#F5C26B", "#9A5B00");

        UpdateSegmentedBarWidths(account);
    }

    public void SetStorageTrackWidth(double width)
    {
        if (width <= 20 || Math.Abs(width - _storageTrackWidth) < 1.0)
        {
            return;
        }

        _storageTrackWidth = width;
        if (_lastAccount.IsConnected)
        {
            UpdateSegmentedBarWidths(_lastAccount);
        }
    }

    private void UpdateSegmentedBarWidths(DriveAccount account)
    {
        var trackWidth = Math.Max(120.0, _storageTrackWidth);
        if (account.IsConnected && account.Quota.LimitBytes > 0)
        {
            var otherBytes = Math.Max(0, account.Quota.UsageBytes - account.Quota.UsageInDriveBytes - account.Quota.UsageInTrashBytes);
            DriveLegendText = Loc.Format("StorageDriveValueFmt", UiConverters.FormatBytes(account.Quota.UsageInDriveBytes));
            OtherLegendText = Loc.Format("StorageOtherValueFmt", UiConverters.FormatBytes(otherBytes));
            TrashLegendText = Loc.Format("StorageTrashValueFmt", UiConverters.FormatBytes(account.Quota.UsageInTrashBytes));

            (DriveBarWidth, OtherBarWidth, TrashBarWidth) = ComputeStorageBarWidths(account.Quota, trackWidth);
        }
        else
        {
            DriveLegendText = Loc.Format("StorageDriveValueFmt", "0 B");
            OtherLegendText = Loc.Format("StorageOtherValueFmt", "0 B");
            TrashLegendText = Loc.Format("StorageTrashValueFmt", "0 B");
            DriveBarWidth = 0;
            OtherBarWidth = 0;
            TrashBarWidth = 0;
        }
    }

    [RelayCommand]
    private Task SyncSelectedFolderAsync() => SelectedFolder is not null
        ? SyncSingleFolderAsync(SelectedFolder.Id)
        : Task.CompletedTask;

    [RelayCommand]
    private Task TogglePauseSelectedFolderAsync() => SelectedFolder is not null
        ? TogglePauseFolderAsync(SelectedFolder.Id, !SelectedFolder.IsPaused)
        : Task.CompletedTask;

    [RelayCommand]
    private Task ToggleModeSelectedFolderAsync() => SelectedFolder is not null
        ? ChangeFolderModeAsync(SelectedFolder.Id, SelectedFolder.Mode == SyncMode.TwoWay ? SyncMode.OneWayBackup : SyncMode.TwoWay)
        : Task.CompletedTask;

    [RelayCommand]
    private Task OpenSelectedFolderAsync() => SelectedFolder is not null
        ? OpenLocalFolderAsync(SelectedFolder.LocalPath)
        : Task.CompletedTask;

    [RelayCommand]
    private void CustomizeSelectedFolder()
    {
        SelectedFolder?.ToggleCustomizeCommand.Execute(null);
    }

    [RelayCommand]
    private void RequestRemoveSelectedFolder()
    {
        if (SelectedFolder is not null)
        {
            PendingRemoveFolderId = SelectedFolder.Id;
        }
    }

    public void RequestRemoveFolder(string folderId)
    {
        PendingRemoveFolderId = folderId;
    }

    [RelayCommand]
    private void CancelRemoveFolder() => PendingRemoveFolderId = null;

    [RelayCommand]
    private async Task ConfirmRemoveFolderAsync()
    {
        var folderId = PendingRemoveFolderId;
        PendingRemoveFolderId = null;
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            await RemoveFolderAsync(folderId);
        }
    }

    [RelayCommand]
    private Task RemoveSelectedFolderAsync() => SelectedFolder is not null
        ? RemoveFolderAsync(SelectedFolder.Id)
        : Task.CompletedTask;

    public Task OpenLocalFolderAsync(string localPath)
    {
        if (OpenLocalFolderCallback is null || string.IsNullOrWhiteSpace(localPath))
        {
            return Task.CompletedTask;
        }

        return OpenLocalFolderCallback.Invoke(localPath);
    }

    [RelayCommand]
    private async Task BuyMoreStorageAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(GoogleOneStorageUrl));
        }
    }

    [RelayCommand]
    private async Task SyncAllAsync()
    {
        if (!_driveClient.IsConnected)
        {
            StatusMessage = Loc.Text("ConnectBeforeSync");
            return;
        }

        await RefreshFoldersAsync();
        if (Folders.Count == 0)
        {
            StatusMessage = Loc.Text("EmptyFoldersHint");
            return;
        }

        if (Folders.All(f => f.IsPaused))
        {
            StatusMessage = Loc.Text("AllFoldersPausedHint");
            return;
        }

        StatusMessage = Loc.Text("SyncingAllFolders");
        try
        {
            await _syncService.TriggerSyncAllAsync();
            await RefreshFoldersAsync();
            StatusMessage = Loc.Text("SyncFinished");
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("FolderMapErrorFmt", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddLocalFolderToRootAsync()
    {
        await MapPickedLocalFoldersIntoRemoteParentAsync("root", Loc.Text("MyDriveLabel"));
    }

    private async Task OnRemoteParentSelectedForAutoSubfolderSyncAsync(string parentFolderId, string parentRemoteDisplay)
    {
        await MapPickedLocalFoldersIntoRemoteParentAsync(parentFolderId, parentRemoteDisplay);
        IsRemoteBrowserActive = false;
    }

    private async Task MapPickedLocalFoldersIntoRemoteParentAsync(string parentFolderId, string parentRemoteDisplay)
    {
        if (!_driveClient.IsConnected)
        {
            StatusMessage = Loc.Text("ConnectBeforeSync");
            return;
        }

        IReadOnlyList<string> selectedPaths;
        if (PickLocalFoldersCallback is not null)
        {
            selectedPaths = await PickLocalFoldersCallback.Invoke();
        }
        else if (PickLocalFolderCallback is not null)
        {
            var single = await PickLocalFolderCallback.Invoke();
            selectedPaths = string.IsNullOrWhiteSpace(single) ? [] : [single];
        }
        else
        {
            return;
        }

        var validPaths = selectedPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (validPaths.Length == 0)
        {
            return;
        }

        try
        {
            var mappedCount = 0;
            var lastRemoteDisplay = parentRemoteDisplay;

            foreach (var localPath in validPaths)
            {
                var folderName = new DirectoryInfo(localPath).Name;
                var targetId = parentFolderId;
                var targetDisplay = parentRemoteDisplay;

                if (_driveClient.IsConnected && !string.IsNullOrWhiteSpace(folderName))
                {
                    var remoteFolder = await _driveClient.FindOrCreateFolderAsync(folderName, parentFolderId);
                    targetId = remoteFolder.Id;
                    targetDisplay = $"{parentRemoteDisplay}/{remoteFolder.Name}";
                }

                await _syncService.AddSyncFolderAsync(localPath, targetId, targetDisplay, SyncMode.TwoWay);
                lastRemoteDisplay = targetDisplay;
                mappedCount++;
            }

            IsRemoteBrowserActive = false;
            await RefreshFoldersAsync();
            StatusMessage = mappedCount == 1
                ? Loc.Format("FolderMappedFmt", validPaths[0], lastRemoteDisplay)
                : Loc.Format("MultipleFoldersMappedFmt", mappedCount, parentRemoteDisplay);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("FolderMapErrorFmt", ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenRemoteBrowserAsync()
    {
        IsRemoteBrowserActive = true;
        await RemoteBrowser.LoadCurrentFolderAsync();
    }

    [RelayCommand]
    private void ShowMappedFolders()
    {
        IsRemoteBrowserActive = false;
    }

    public async Task SyncSingleFolderAsync(string folderId)
    {
        await _syncService.TriggerFolderSyncAsync(folderId);
        await RefreshFoldersAsync();
    }

    public async Task TogglePauseFolderAsync(string folderId, bool pause)
    {
        await _syncService.SetFolderPausedAsync(folderId, pause);
        await RefreshFoldersAsync();
    }

    public async Task ChangeFolderModeAsync(string folderId, SyncMode mode)
    {
        await _syncService.UpdateSyncModeAsync(folderId, mode);
        await RefreshFoldersAsync();
    }

    public async Task SaveFolderCustomizationAsync(string folderId, string? customAlias, string? customColorHex, string? customIconKey)
    {
        await _syncService.UpdateFolderCustomizationAsync(folderId, customAlias, customColorHex, customIconKey);
        await RefreshFoldersAsync();
    }

    public async Task RemoveFolderAsync(string folderId)
    {
        await _syncService.RemoveSyncFolderAsync(folderId);
        await RefreshFoldersAsync();
    }

    private async Task OnRemoteFolderSelectedAsync(string remoteFolderId, string remoteFolderName)
    {
        await PromptAndMapFolderAsync(remoteFolderId, remoteFolderName);
        IsRemoteBrowserActive = false;
    }

    private async Task PromptAndMapFolderAsync(string remoteFolderId, string remoteFolderName)
    {
        if (PickLocalFolderCallback is null)
        {
            return;
        }

        var localPath = await PickLocalFolderCallback.Invoke();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        try
        {
            await _syncService.AddSyncFolderAsync(localPath, remoteFolderId, remoteFolderName, SyncMode.TwoWay);
            await RefreshFoldersAsync();
            StatusMessage = Loc.Format("FolderMappedFmt", localPath, remoteFolderName);
        }
        catch (Exception ex)
        {
            StatusMessage = Loc.Format("FolderMapErrorFmt", ex.Message);
        }
    }
}
