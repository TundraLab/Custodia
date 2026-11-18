using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TundraDrive.Api;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Models;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;
using TundraDrive.Desktop.Converters;
using TundraDrive.Desktop.Localization;
using TundraDrive.Desktop.Services;

namespace TundraDrive.Desktop.ViewModels;

public sealed partial class SettingsAccountItemViewModel : ObservableObject
{
    public string Id { get; }
    public string DisplayName { get; }
    public string EmailAddress { get; }
    public string QuotaSummary { get; }
    public bool IsActive { get; }
    public bool CanSwitch => !IsActive;
    public string ActiveBadgeText { get; }
    public string SwitchButtonText { get; }
    public string RevokeButtonText { get; }
    public IAsyncRelayCommand SwitchCommand { get; }
    public IAsyncRelayCommand RevokeCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private Bitmap? _avatarBitmap;

    public bool HasAvatar => AvatarBitmap is not null;

    public SettingsAccountItemViewModel(
        DriveAccount account,
        bool isActive,
        ILocalizationService loc,
        Func<string, Task> onSwitch,
        Func<string, Task> onRevoke)
    {
        Id = account.Id;
        DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? account.EmailAddress : account.DisplayName;
        EmailAddress = account.EmailAddress;
        var limitText = account.Quota.LimitBytes > 0
            ? UiConverters.FormatBytes(account.Quota.LimitBytes)
            : loc.Text("Unlimited");
        QuotaSummary = loc.Format("QuotaUsageFmt", UiConverters.FormatBytes(account.Quota.UsageBytes), limitText);
        IsActive = isActive;
        ActiveBadgeText = loc.Text("ActiveAccountBadge");
        SwitchButtonText = loc.Text("SwitchToAccountButton");
        RevokeButtonText = loc.Text("RevokeAccountButton");
        SwitchCommand = new AsyncRelayCommand(() => onSwitch(account.Id));
        RevokeCommand = new AsyncRelayCommand(() => onRevoke(account.Id));

        if (!string.IsNullOrWhiteSpace(account.PhotoUrl))
        {
            _ = LoadAvatarAsync(account.PhotoUrl);
        }
    }

    private async Task LoadAvatarAsync(string photoUrl)
    {
        var bmp = await AvatarLoader.LoadAsync(photoUrl);
        if (bmp is not null)
        {
            Dispatcher.UIThread.Post(() => AvatarBitmap = bmp);
        }
    }
}

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settingsService;
    private readonly IEmbeddedApiServer _apiServer;
    private readonly IAutostartManager _autostartManager;
    private readonly IDesktopNotificationService _notificationService;
    private readonly IAuthCredentialManager _authManager;
    private readonly IDriveSyncService _syncService;
    private readonly MemoryCleanerService _memoryCleaner;
    private CancellationTokenSource? _saveFeedbackCts;
    private CancellationTokenSource? _reloadFeedbackCts;

    public ILocalizationService Loc { get; }

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UiScaleDisplay))]
    private int _uiScalePercent = 100;

    public string UiScaleDisplay => $"{Math.Clamp(UiScalePercent, 100, 300)}%";

    public event EventHandler<int>? UiScaleChanged;

    [ObservableProperty]
    private bool _startOnBoot;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose = true;

    [ObservableProperty]
    private string _downloadDirectory = AppPaths.DefaultDownloadDirectory;

    [ObservableProperty]
    private bool _isAutostartSupported;

    [ObservableProperty]
    private decimal? _syncIntervalSeconds = 120;

    [ObservableProperty]
    private bool _watchLocalChanges = true;

    [ObservableProperty]
    private bool _embeddedApiEnabled = true;

    [ObservableProperty]
    private decimal? _embeddedApiPort = 5080;

    [ObservableProperty]
    private bool _autoMemoryCleanupEnabled = true;

    [ObservableProperty]
    private decimal? _memoryCleanupIntervalMinutes = 5;

    [ObservableProperty]
    private string _memoryCleanerStatusText = string.Empty;

    [ObservableProperty]
    private bool _isCleaningMemory;

    [ObservableProperty]
    private int _selectedConflictPolicyIndex;

    [ObservableProperty]
    private string _ignorePatternsText = string.Empty;

    [ObservableProperty]
    private string _apiEndpointStatus = string.Empty;

    [ObservableProperty]
    private string _credentialsPathDisplay = string.Empty;

    [ObservableProperty]
    private string _tokensPathDisplay = string.Empty;

    [ObservableProperty]
    private string _databasePathDisplay = string.Empty;

    [ObservableProperty]
    private string _autostartPathDisplay = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowDefaultSaveIcon))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowDefaultSaveIcon))]
    private bool _isSavedFeedback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReloadButtonText))]
    private bool _isReloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReloadButtonText))]
    private bool _isReloadedFeedback;

    public bool ShowDefaultSaveIcon => !IsSaving && !IsSavedFeedback;

    public string SaveButtonText =>
        IsSaving ? Loc.SavingButton :
        IsSavedFeedback ? Loc.SavedButton :
        Loc.SaveChangesButton;

    public string ReloadButtonText =>
        IsReloading ? Loc.ReloadingButton :
        IsReloadedFeedback ? Loc.ReloadedButton :
        Loc.ReloadButton;

    public string RepositoryUrl => MainWindowViewModel.RepositoryUrl;
    public Func<Uri, Task>? OpenExternalUriCallback { get; set; }
    public Func<string, Task>? OpenLocalFolderCallback { get; set; }
    public Func<Task<string?>>? PickDownloadDirectoryCallback { get; set; }
    public Func<Task<string?>>? PickCredentialsFileCallback { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialsStatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(CanRevokeCredentialsJson))]
    private bool _hasCredentialsJson;

    [ObservableProperty]
    private string _credentialsJsonStatusText = string.Empty;

    [ObservableProperty]
    private string _credentialsFilePathDisplay = AppPaths.CredentialsFilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRevokeCredentialsJson))]
    private bool _isCredentialsBusy;

    [ObservableProperty]
    private bool _isAddAccountPromptVisible;

    public string CredentialsStatusBadgeText => HasCredentialsJson
        ? Loc.Text("CredentialsActiveBadge")
        : Loc.Text("CredentialsMissingBadge");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectedAccounts))]
    private int _connectedAccountsCount;

    public bool HasConnectedAccounts => ConnectedAccountsCount > 0;
    public bool CanRevokeCredentialsJson => HasCredentialsJson && !IsCredentialsBusy;

    public ObservableCollection<SettingsAccountItemViewModel> ConnectedAccounts { get; } = [];

    public IReadOnlyList<string> LanguageOptions { get; } =
    [
        "Português (Brasil)",
        "English (US)"
    ];

    public ObservableCollection<string> ConflictPolicies { get; } = [];

    public SettingsViewModel(
        IAppSettingsService settingsService,
        IEmbeddedApiServer apiServer,
        IAutostartManager autostartManager,
        ILocalizationService localization,
        IDesktopNotificationService notificationService,
        IAuthCredentialManager authManager,
        IDriveSyncService syncService,
        MemoryCleanerService memoryCleaner)
    {
        _settingsService = settingsService;
        _apiServer = apiServer;
        _autostartManager = autostartManager;
        _notificationService = notificationService;
        _authManager = authManager;
        _syncService = syncService;
        _memoryCleaner = memoryCleaner;
        Loc = localization;

        IsAutostartSupported = _autostartManager.IsSupported;
        RebuildLocalizedLists();

        Loc.LanguageChanged += (_, _) =>
        {
            RebuildLocalizedLists();
            RefreshPathAndApiTexts();
            RefreshCredentialsAndAccounts();
            RefreshMemoryCleanerStatus();
            OnPropertyChanged(nameof(SaveButtonText));
            OnPropertyChanged(nameof(ReloadButtonText));
        };

        _syncService.AccountsListChanged += (_, _) => Dispatcher.UIThread.Post(RefreshCredentialsAndAccounts);
        _syncService.AccountChanged += (_, _) => Dispatcher.UIThread.Post(RefreshCredentialsAndAccounts);
        _memoryCleaner.CleanupCompleted += (_, _) => Dispatcher.UIThread.Post(RefreshMemoryCleanerStatus);

        LoadFromCurrent();
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        var code = value == 1 ? "en-US" : "pt-BR";
        Loc.SetLanguage(code);
    }

    partial void OnUiScalePercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 100, 300);
        if (clamped != value)
        {
            UiScalePercent = clamped;
            return;
        }

        UiScaleChanged?.Invoke(this, clamped);
    }

    private async Task PersistUiScaleAsync(int scalePercent)
    {
        try
        {
            var current = _settingsService.Current;
            current.UiScalePercent = scalePercent;
            await _settingsService.SaveAsync(current);
        }
        catch
        {
        }
    }

    [RelayCommand]
    private void ResetUiScale()
    {
        UiScalePercent = 100;
        _ = PersistUiScaleAsync(100);
    }

    public void AdjustUiScale(int delta, bool persistImmediately = true)
    {
        var clamped = Math.Clamp(UiScalePercent + delta, 100, 300);
        UiScalePercent = clamped;
        if (persistImmediately)
        {
            _ = PersistUiScaleAsync(clamped);
        }
    }

    public void LoadFromCurrent()
    {
        var current = _settingsService.Current;
        SelectedLanguageIndex = string.Equals(current.Language, "en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        UiScalePercent = Math.Clamp(current.UiScalePercent, 100, 300);
        StartOnBoot = current.StartOnBoot || _autostartManager.IsEnabled();
        NotificationsEnabled = current.NotificationsEnabled;
        MinimizeToTrayOnClose = current.MinimizeToTrayOnClose;
        DownloadDirectory = string.IsNullOrWhiteSpace(current.DownloadDirectory)
            ? AppPaths.DefaultDownloadDirectory
            : current.DownloadDirectory;
        SyncIntervalSeconds = current.SyncIntervalSeconds;
        WatchLocalChanges = current.WatchLocalChanges;
        EmbeddedApiEnabled = current.EmbeddedApiEnabled;
        EmbeddedApiPort = current.EmbeddedApiPort;
        AutoMemoryCleanupEnabled = current.AutoMemoryCleanupEnabled;
        MemoryCleanupIntervalMinutes = Math.Clamp(current.MemoryCleanupIntervalMinutes, 1, 60);
        SelectedConflictPolicyIndex = (int)current.ConflictPolicy;
        IgnorePatternsText = string.Join(Environment.NewLine, current.IgnorePatterns);

        RefreshPathAndApiTexts();
        RefreshCredentialsAndAccounts();
        RefreshMemoryCleanerStatus();
    }

    private void RefreshMemoryCleanerStatus()
    {
        var currentMb = MemoryCleanerService.GetCurrentProcessPrivateMemoryBytes() / 1_000_000.0;
        if (_memoryCleaner.LastCleanedAtUtc.HasValue)
        {
            var freedMb = _memoryCleaner.LastFreedBytes / 1_000_000.0;
            MemoryCleanerStatusText = Loc.Format("MemoryStatusCleanedFmt", currentMb, freedMb);
        }
        else
        {
            MemoryCleanerStatusText = Loc.Format("MemoryStatusCurrentFmt", currentMb);
        }
    }

    [RelayCommand]
    private async Task CleanMemoryNowAsync()
    {
        if (IsCleaningMemory)
        {
            return;
        }

        IsCleaningMemory = true;
        try
        {
            var result = await _memoryCleaner.CleanNowAsync(forceReleaseMedia: true);
            var afterMb = result.AfterBytes / 1_000_000.0;
            var freedMb = result.FreedBytes / 1_000_000.0;
            MemoryCleanerStatusText = Loc.Format("MemoryStatusCleanedFmt", afterMb, freedMb);
        }
        finally
        {
            IsCleaningMemory = false;
        }
    }

    public void RefreshCredentialsAndAccounts()
    {
        HasCredentialsJson = _authManager.CredentialsFileExists();
        CredentialsFilePathDisplay = _authManager.CredentialsFilePath;
        CredentialsJsonStatusText = HasCredentialsJson
            ? Loc.Format("CredentialsJsonStatusReadyFmt", _authManager.CredentialsFilePath)
            : Loc.Text("CredentialsJsonStatusMissing");
        OnPropertyChanged(nameof(CredentialsStatusBadgeText));
        OnPropertyChanged(nameof(CanRevokeCredentialsJson));

        ConnectedAccounts.Clear();
        var activeId = _syncService.CurrentAccount.IsConnected ? _syncService.CurrentAccount.Id : null;
        foreach (var account in _syncService.SavedAccounts.Where(a => a.IsConnected))
        {
            ConnectedAccounts.Add(new SettingsAccountItemViewModel(
                account,
                string.Equals(account.Id, activeId, StringComparison.Ordinal),
                Loc,
                SwitchAccountFromSettingsAsync,
                RevokeSingleAccountAsync));
        }

        ConnectedAccountsCount = ConnectedAccounts.Count;
    }

    private async Task SwitchAccountFromSettingsAsync(string accountId)
    {
        IsCredentialsBusy = true;
        try
        {
            await _syncService.SwitchAccountAsync(accountId);
            RefreshCredentialsAndAccounts();
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    private async Task RevokeSingleAccountAsync(string accountId)
    {
        IsCredentialsBusy = true;
        try
        {
            await _syncService.DisconnectAccountAsync(accountId);
            RefreshCredentialsAndAccounts();
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAccountFromSettingsAsync()
    {
        if (!_authManager.CredentialsFileExists())
        {
            await ImportJsonAndAddAccountFromSettingsAsync();
            return;
        }

        IsAddAccountPromptVisible = !IsAddAccountPromptVisible;
    }

    [RelayCommand]
    private async Task ImportJsonAndAddAccountFromSettingsAsync()
    {
        IsAddAccountPromptVisible = false;
        if (PickCredentialsFileCallback is null)
        {
            return;
        }

        var path = await PickCredentialsFileCallback.Invoke();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        IsCredentialsBusy = true;
        try
        {
            await _syncService.ConnectAccountAsync(addNewAccount: true, customCredentialsJsonPath: path);
            RefreshCredentialsAndAccounts();
        }
        catch
        {
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ContinueWithCurrentJsonFromSettingsAsync()
    {
        IsAddAccountPromptVisible = false;
        IsCredentialsBusy = true;
        try
        {
            await _syncService.ConnectAccountAsync(addNewAccount: true);
            RefreshCredentialsAndAccounts();
        }
        catch
        {
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelAddAccountPromptFromSettings() => IsAddAccountPromptVisible = false;

    [RelayCommand]
    private async Task ImportCredentialsJsonAsync()
    {
        if (PickCredentialsFileCallback is null)
        {
            return;
        }

        var selected = await PickCredentialsFileCallback.Invoke();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        IsCredentialsBusy = true;
        try
        {
            await _authManager.ImportCredentialsFileAsync(selected);
            RefreshCredentialsAndAccounts();
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenCredentialsFolderAsync()
    {
        if (OpenLocalFolderCallback is null)
        {
            return;
        }

        var dir = Path.GetDirectoryName(_authManager.CredentialsFilePath);
        await OpenLocalFolderCallback.Invoke(string.IsNullOrWhiteSpace(dir) ? AppPaths.ConfigDirectory : dir);
    }

    [RelayCommand]
    private async Task RevokeCredentialsJsonAsync()
    {
        IsCredentialsBusy = true;
        try
        {
            await _syncService.RevokeCredentialsFileAsync();
            RefreshCredentialsAndAccounts();
        }
        finally
        {
            IsCredentialsBusy = false;
        }
    }

    private void RebuildLocalizedLists()
    {
        var savedIndex = Math.Max(0, SelectedConflictPolicyIndex);
        ConflictPolicies.Clear();
        ConflictPolicies.Add(Loc.Text("ConflictKeepNewest"));
        ConflictPolicies.Add(Loc.Text("ConflictPreferLocal"));
        ConflictPolicies.Add(Loc.Text("ConflictPreferRemote"));
        ConflictPolicies.Add(Loc.Text("ConflictRenameCopy"));
        SelectedConflictPolicyIndex = Math.Clamp(savedIndex, 0, ConflictPolicies.Count - 1);
    }

    private void RefreshPathAndApiTexts()
    {
        ApiEndpointStatus = _apiServer.IsRunning
            ? Loc.Format("ApiStatusActiveFmt", _apiServer.BoundAddress)
            : string.IsNullOrWhiteSpace(_apiServer.LastError)
                ? Loc.Text("ApiStatusInactive")
                : Loc.Format("ApiStatusErrorFmt", _apiServer.LastError);

        CredentialsPathDisplay = Loc.Format("PathCredsFmt", AppPaths.CredentialsFilePath);
        TokensPathDisplay = Loc.Format("PathTokensFmt", AppPaths.TokensDirectory);
        DatabasePathDisplay = Loc.Format("PathDbFmt", AppPaths.DatabaseFilePath);
        AutostartPathDisplay = Loc.Format("PathAutostartFmt", _autostartManager.DesktopEntryPath);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        _saveFeedbackCts?.Cancel();
        _saveFeedbackCts?.Dispose();
        _saveFeedbackCts = new CancellationTokenSource();
        var token = _saveFeedbackCts.Token;

        IsSavedFeedback = false;
        IsSaving = true;

        try
        {
            var patterns = IgnorePatternsText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var portValue = (int)(EmbeddedApiPort ?? 5080);
            var intervalValue = (int)(SyncIntervalSeconds ?? 120);
            var languageCode = SelectedLanguageIndex == 1 ? "en-US" : "pt-BR";

            var updated = new AppSettings
            {
                Language = languageCode,
                ActiveAccountId = _settingsService.Current.ActiveAccountId,
                UiScalePercent = Math.Clamp(UiScalePercent, 100, 300),
                StartOnBoot = StartOnBoot,
                NotificationsEnabled = NotificationsEnabled,
                MinimizeToTrayOnClose = MinimizeToTrayOnClose,
                DownloadDirectory = string.IsNullOrWhiteSpace(DownloadDirectory)
                    ? AppPaths.DefaultDownloadDirectory
                    : Path.GetFullPath(DownloadDirectory.Trim()),
                SyncIntervalSeconds = Math.Clamp(intervalValue, 30, 86400),
                WatchLocalChanges = WatchLocalChanges,
                EmbeddedApiEnabled = EmbeddedApiEnabled,
                EmbeddedApiPort = portValue is >= 1024 and <= 65535 ? portValue : 5080,
                AutoMemoryCleanupEnabled = AutoMemoryCleanupEnabled,
                MemoryCleanupIntervalMinutes = Math.Clamp((int)(MemoryCleanupIntervalMinutes ?? 5), 1, 60),
                ConflictPolicy = (ConflictResolutionPolicy)Math.Clamp(SelectedConflictPolicyIndex, 0, 3),
                IgnorePatterns = patterns
            };

            _autostartManager.SetEnabled(StartOnBoot);
            await _settingsService.SaveAsync(updated);
            _memoryCleaner.NotifySettingsUpdated();
            Loc.SetLanguage(languageCode);

            // Brief pause so the spinning indicator is clearly perceived before switching to "Saved!".
            await Task.Delay(320, token);
            IsSaving = false;
            IsSavedFeedback = true;
            StatusMessage = string.Empty;
            _ = ClearSaveFeedbackLaterAsync(token);
        }
        catch (OperationCanceledException)
        {
            IsSaving = false;
        }
    }

    private async Task ClearSaveFeedbackLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(2000, token);
            IsSavedFeedback = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task ReloadSettingsAsync()
    {
        _reloadFeedbackCts?.Cancel();
        _reloadFeedbackCts?.Dispose();
        _reloadFeedbackCts = new CancellationTokenSource();
        var token = _reloadFeedbackCts.Token;

        IsReloadedFeedback = false;
        IsReloading = true;

        try
        {
            await _settingsService.LoadAsync();
            LoadFromCurrent();

            await Task.Delay(420, token);
            IsReloading = false;
            IsReloadedFeedback = true;
            StatusMessage = string.Empty;
            _ = ClearReloadFeedbackLaterAsync(token);
        }
        catch (OperationCanceledException)
        {
            IsReloading = false;
        }
    }

    private async Task ClearReloadFeedbackLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(1800, token);
            IsReloadedFeedback = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task BrowseDownloadDirectoryAsync()
    {
        if (PickDownloadDirectoryCallback is null)
        {
            return;
        }

        var selected = await PickDownloadDirectoryCallback.Invoke();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            DownloadDirectory = selected;
        }
    }

    [RelayCommand]
    private async Task TestNotificationAsync()
    {
        await _notificationService.NotifyAsync(
            Loc.Text("TestNotificationTitle"),
            Loc.Text("TestNotificationBody"),
            NotificationUrgency.Normal,
            bypassThrottle: true);
    }

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        if (OpenExternalUriCallback is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(RepositoryUrl));
        }
    }

    [RelayCommand]
    private async Task OpenApiDocsAsync()
    {
        if (OpenExternalUriCallback is null)
        {
            return;
        }

        var pdfPath = ResolveLocalDocsPath("TundraDrive-API.pdf");
        if (pdfPath is not null)
        {
            await OpenExternalUriCallback.Invoke(new Uri(pdfPath));
            return;
        }

        var localDocsPath = ResolveLocalDocsPath("docs", "API.md");
        var target = localDocsPath is not null
            ? new Uri(localDocsPath)
            : new Uri($"{RepositoryUrl}/blob/main/TundraDrive-API.pdf");
        await OpenExternalUriCallback.Invoke(target);
    }

    [RelayCommand]
    private async Task OpenApiSpecAsync()
    {
        if (OpenExternalUriCallback is not null && _apiServer.IsRunning)
        {
            await OpenExternalUriCallback.Invoke(new Uri($"{_apiServer.BoundAddress}/api/v1/openapi.json"));
        }
    }

    private static string? ResolveLocalDocsPath(params string[] segments)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var depth = 0; depth < 6 && dir is not null; depth++, dir = dir.Parent)
            {
                var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
