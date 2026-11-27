using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Notifications;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Security;

namespace TundraDrive.Core.Sync;

public sealed class DriveSyncService : IDriveSyncService, IDisposable
{
    private readonly IGoogleDriveClient _driveClient;
    private readonly ISyncStateRepository _repository;
    private readonly IAppSettingsService _settingsService;
    private readonly IAuthCredentialManager _authManager;
    private readonly LocalFolderWatcherService _watcherService;
    private readonly ILogger<DriveSyncService> _logger;
    private readonly ITundraMailerClient? _mailerClient;
    private readonly Func<CancellationToken, Task<bool>>? _connectivityProbe;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _folderLocks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _folderSyncCancellations = new();
    private readonly ConcurrentDictionary<string, TransferQueueItem> _transfers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _transferCancellations = new();
    private readonly ConcurrentDictionary<string, byte> _pendingInitialSyncFolders = new();
    private readonly object _suppressionLock = new();
    private readonly Dictionary<string, int> _suppressedWatcherCounts = new(StringComparer.Ordinal);
    private readonly object _downloadPathLock = new();
    private readonly HashSet<string> _reservedDownloadPaths = new(StringComparer.Ordinal);
    private readonly TransferBandwidthLimiter _bandwidthLimiter = new();

    private PeriodicTimer? _periodicTimer;
    private CancellationTokenSource? _lifecycleCts;
    private int _activeSyncCount;
    private int _prunedCompletedCount;
    private int _prunedFailedCount;
    private int _prunedCancelledCount;
    private int _isOnlineFlag = 1;
    private int _probeInFlight;
    private static readonly HttpClient ConnectivityProbeClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public DriveSyncService(
        IGoogleDriveClient driveClient,
        ISyncStateRepository repository,
        IAppSettingsService settingsService,
        IAuthCredentialManager authManager,
        LocalFolderWatcherService watcherService,
        ILogger<DriveSyncService> logger,
        ITundraMailerClient? mailerClient = null,
        Func<CancellationToken, Task<bool>>? connectivityProbe = null)
    {
        _driveClient = driveClient;
        _repository = repository;
        _settingsService = settingsService;
        _authManager = authManager;
        _watcherService = watcherService;
        _logger = logger;
        _mailerClient = mailerClient;
        _connectivityProbe = connectivityProbe;

        _isOnlineFlag = connectivityProbe is not null || NetworkInterface.GetIsNetworkAvailable() ? 1 : 0;
        _watcherService.FolderChangeDetected += OnLocalFolderChangedAsync;
        _settingsService.SettingsChanged += OnSettingsChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    public DriveAccount CurrentAccount { get; private set; } = new() { IsConnected = false };
    public IReadOnlyList<DriveAccount> SavedAccounts { get; private set; } = [];
    public bool IsSyncRunning => Volatile.Read(ref _activeSyncCount) > 0;
    public bool IsOnline => Volatile.Read(ref _isOnlineFlag) == 1;

    public event EventHandler<DriveAccount>? AccountChanged;
    public event EventHandler? AccountsListChanged;
    public event EventHandler? SyncFoldersChanged;
    public event EventHandler<TransferQueueItem>? TransferUpdated;
    public event EventHandler<SyncActivityLog>? ActivityLogged;
    public event EventHandler<bool>? ConnectivityChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _settingsService.LoadAsync(cancellationToken);
        await ProbeConnectivityAsync(cancellationToken);

        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);
        foreach (var savedAcc in SavedAccounts.Where(a => a.IsConnected))
        {
            await _authManager.OrganizeAccountDirectoryAsync(savedAcc.Id, savedAcc.EmailAddress, savedAcc.DisplayName, cancellationToken);
        }

        var targetAccountId = string.IsNullOrWhiteSpace(_settingsService.Current.ActiveAccountId)
            ? "default"
            : _settingsService.Current.ActiveAccountId;

        var candidateIds = new List<string> { targetAccountId };
        foreach (var saved in SavedAccounts)
        {
            if (!candidateIds.Contains(saved.Id, StringComparer.Ordinal))
            {
                candidateIds.Add(saved.Id);
            }
        }

        DriveAccount? restored = null;
        foreach (var candidateId in candidateIds)
        {
            restored = await _driveClient.TryRestoreSessionAsync(candidateId, cancellationToken);
            if (restored is not null)
            {
                break;
            }
        }

        if (restored is not null)
        {
            var cached = SavedAccounts.FirstOrDefault(a =>
                string.Equals(a.Id, restored.Id, StringComparison.Ordinal));
            if (cached is not null && string.IsNullOrWhiteSpace(restored.EmailAddress))
            {
                restored.DisplayName = string.IsNullOrWhiteSpace(cached.DisplayName)
                    ? restored.DisplayName
                    : cached.DisplayName;
                restored.EmailAddress = cached.EmailAddress;
                restored.PhotoUrl = cached.PhotoUrl;
                restored.Quota = cached.Quota;
            }

            CurrentAccount = restored;
            await _authManager.OrganizeAccountDirectoryAsync(restored.Id, restored.EmailAddress, restored.DisplayName, cancellationToken);
            await _repository.UpsertAccountAsync(restored, cancellationToken);
            await PersistActiveAccountIdAsync(restored.Id, cancellationToken);
            SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);

            var folders = await RestoreAndSyncAccountFoldersAsync(restored, cancellationToken);
            foreach (var folder in folders)
            {
                if (folder.State == FolderSyncState.Syncing)
                {
                    folder.State = folder.IsPaused ? FolderSyncState.Paused : FolderSyncState.Idle;
                    await _repository.UpsertSyncFolderAsync(folder, cancellationToken);
                }
            }

            _watcherService.UpdateWatchers(folders);
            AccountsListChanged?.Invoke(this, EventArgs.Empty);
            AccountChanged?.Invoke(this, restored);
            SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

            await LogAsync(ActivitySeverity.Success, "AuthRestore",
                $"Sessão restaurada para {restored.EmailAddress}.", null, null, cancellationToken);
        }
        else
        {
            CurrentAccount = new DriveAccount { Id = targetAccountId, IsConnected = false };
            _watcherService.UpdateWatchers([]);
            AccountsListChanged?.Invoke(this, EventArgs.Empty);
            AccountChanged?.Invoke(this, CurrentAccount);
            SyncFoldersChanged?.Invoke(this, EventArgs.Empty);
        }

        StartBackgroundTimer();
    }

    public async Task<DriveAccount> ConnectAccountAsync(
        bool addNewAccount = false,
        string? customCredentialsJsonPath = null,
        CancellationToken cancellationToken = default)
    {
        var existingAccounts = await _repository.GetAllAccountsAsync(cancellationToken);
        var candidateKey = !addNewAccount && existingAccounts.Count == 0 && !await _authManager.HasStoredTokenAsync("default")
            ? "default"
            : $"acc_{Guid.NewGuid():N}"[..16];

        var hasCustomJson = !string.IsNullOrWhiteSpace(customCredentialsJsonPath);
        if (hasCustomJson)
        {
            await _authManager.ImportCredentialsFileAsync(customCredentialsJsonPath!, candidateKey, cancellationToken: cancellationToken);
        }

        DriveAccount account;
        try
        {
            account = await _driveClient.ConnectInteractiveAsync(candidateKey, cancellationToken);
        }
        catch
        {
            if (hasCustomJson)
            {
                _authManager.DeleteAccountCredentialsFile(candidateKey);
            }
            throw;
        }

        var duplicate = existingAccounts.FirstOrDefault(a =>
            !string.Equals(a.Id, candidateKey, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(a.EmailAddress) &&
            string.Equals(a.EmailAddress, account.EmailAddress, StringComparison.OrdinalIgnoreCase));

        if (duplicate is not null)
        {
            if (hasCustomJson)
            {
                await _authManager.CopyCredentialsAsync(candidateKey, duplicate.Id, account.EmailAddress, cancellationToken);
                _authManager.DeleteAccountCredentialsFile(candidateKey);
            }
            await _authManager.CopyTokenAsync(candidateKey, duplicate.Id, cancellationToken);
            await _authManager.DeleteLocalTokenAsync(candidateKey, cancellationToken);
            account.Id = duplicate.Id;
            await _driveClient.SwitchSessionAsync(duplicate.Id, cancellationToken);
        }

        await _authManager.OrganizeAccountDirectoryAsync(account.Id, account.EmailAddress, account.DisplayName, cancellationToken);

        CancelAllActiveOperations();

        CurrentAccount = account;
        await _repository.UpsertAccountAsync(account, cancellationToken);
        await PersistActiveAccountIdAsync(account.Id, cancellationToken);
        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);

        var folders = await RestoreAndSyncAccountFoldersAsync(account, cancellationToken);
        _watcherService.UpdateWatchers(folders);

        AccountsListChanged?.Invoke(this, EventArgs.Empty);
        AccountChanged?.Invoke(this, account);
        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

        await LogAsync(ActivitySeverity.Success, "AccountConnect",
            $"Conta Google conectada: {account.EmailAddress}.", null, null, cancellationToken);

        if (_settingsService.Current.GmailAccountAlertsEnabled &&
            !string.IsNullOrWhiteSpace(account.EmailAddress))
        {
            _ = TrySendLifecycleEmailAsync(account.EmailAddress, account.DisplayName, isConnected: true);
        }

        if (folders.Any(f => !f.IsPaused))
        {
            _ = Task.Run(() => TriggerSyncAllAsync(), CancellationToken.None);
        }
        return account;
    }

    public async Task<DriveAccount> ReconnectAccountAsync(
        string accountId,
        string? customCredentialsJsonPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return await ConnectAccountAsync(addNewAccount: false, customCredentialsJsonPath, cancellationToken);
        }

        var existing = (await _repository.GetAllAccountsAsync(cancellationToken))
            .FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.Ordinal));
        var knownEmail = existing?.EmailAddress;

        if (!string.IsNullOrWhiteSpace(customCredentialsJsonPath))
        {
            await _authManager.ImportCredentialsFileAsync(customCredentialsJsonPath, accountId, knownEmail, cancellationToken);
        }

        await _authManager.DeleteLocalTokenAsync(accountId, cancellationToken);
        var account = await _driveClient.ConnectInteractiveAsync(accountId, cancellationToken);
        account.Id = accountId;

        await _authManager.OrganizeAccountDirectoryAsync(account.Id, account.EmailAddress, account.DisplayName, cancellationToken);
        CancelAllActiveOperations();

        CurrentAccount = account;
        await _repository.UpsertAccountAsync(account, cancellationToken);
        await PersistActiveAccountIdAsync(account.Id, cancellationToken);
        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);

        var folders = await RestoreAndSyncAccountFoldersAsync(account, cancellationToken);
        _watcherService.UpdateWatchers(folders);

        AccountsListChanged?.Invoke(this, EventArgs.Empty);
        AccountChanged?.Invoke(this, account);
        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

        await LogAsync(ActivitySeverity.Success, "AccountConnect",
            $"Conta Google reconectada: {account.EmailAddress}.", null, null, cancellationToken);

        if (_settingsService.Current.GmailAccountAlertsEnabled &&
            !string.IsNullOrWhiteSpace(account.EmailAddress))
        {
            _ = TrySendLifecycleEmailAsync(account.EmailAddress, account.DisplayName, isConnected: true);
        }

        if (folders.Any(f => !f.IsPaused))
        {
            _ = Task.Run(() => TriggerSyncAllAsync(), CancellationToken.None);
        }

        return account;
    }

    public async Task<DriveAccount?> SwitchAccountAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return null;
        }

        if (_driveClient.IsConnected && string.Equals(CurrentAccount.Id, accountId, StringComparison.Ordinal))
        {
            return await RefreshAccountInfoAsync(cancellationToken);
        }

        CancelAllActiveOperations();
        _watcherService.UpdateWatchers([]);

        var restored = await _driveClient.SwitchSessionAsync(accountId, cancellationToken)
            ?? await _driveClient.ConnectInteractiveAsync(accountId, cancellationToken);

        CurrentAccount = restored;
        await _authManager.OrganizeAccountDirectoryAsync(restored.Id, restored.EmailAddress, restored.DisplayName, cancellationToken);
        await _repository.UpsertAccountAsync(restored, cancellationToken);
        await PersistActiveAccountIdAsync(restored.Id, cancellationToken);
        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);

        var folders = await RestoreAndSyncAccountFoldersAsync(restored, cancellationToken);
        _watcherService.UpdateWatchers(folders);

        AccountsListChanged?.Invoke(this, EventArgs.Empty);
        AccountChanged?.Invoke(this, restored);
        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

        await LogAsync(ActivitySeverity.Success, "AccountSwitch",
            $"Conta ativa alternada para {restored.EmailAddress}.", null, null, cancellationToken);

        return restored;
    }

    public async Task DisconnectAccountAsync(
        string? accountId = null,
        bool deleteFolderConfiguration = false,
        CancellationToken cancellationToken = default)
    {
        var targetId = string.IsNullOrWhiteSpace(accountId) ? CurrentAccount.Id : accountId;
        var isDisconnectingActive = string.Equals(targetId, CurrentAccount.Id, StringComparison.Ordinal);
        var savedList = await _repository.GetAllAccountsAsync(cancellationToken);
        var targetAccount = savedList.FirstOrDefault(a => string.Equals(a.Id, targetId, StringComparison.Ordinal));
        var disconnectedEmail = targetAccount?.EmailAddress ?? (isDisconnectingActive ? CurrentAccount.EmailAddress : string.Empty);

        if (isDisconnectingActive)
        {
            CancelAllActiveOperations();
            _watcherService.UpdateWatchers([]);
        }

        if (_settingsService.Current.GmailAccountAlertsEnabled &&
            !string.IsNullOrWhiteSpace(disconnectedEmail))
        {
            var disconnectedName = targetAccount?.DisplayName ?? (isDisconnectingActive ? CurrentAccount.DisplayName : disconnectedEmail);
            await TrySendLifecycleEmailAsync(disconnectedEmail, disconnectedName, isConnected: false, cancellationToken);
        }

        await _driveClient.DisconnectAsync(targetId, cancellationToken);
        await _authManager.RevokeAndClearAsync(targetId, disconnectedEmail, purgeFolderConfig: deleteFolderConfiguration, cancellationToken);
        if (deleteFolderConfiguration)
        {
            var accountFolders = await _repository.GetSyncFoldersByAccountAsync(targetId, cancellationToken);
            foreach (var af in accountFolders)
            {
                if (_folderSyncCancellations.TryGetValue(af.Id, out var folderCts))
                {
                    folderCts.Cancel();
                }

                var folderLock = _folderLocks.GetOrAdd(af.Id, _ => new SemaphoreSlim(1, 1));
                await folderLock.WaitAsync(cancellationToken);
                folderLock.Release();
            }

            await _repository.DeleteSyncFoldersByAccountAsync(targetId, disconnectedEmail, cancellationToken);
        }
        await _repository.DeleteAccountAsync(targetId, cancellationToken);

        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);

        if (isDisconnectingActive)
        {
            DriveAccount? fallbackAccount = null;
            foreach (var candidate in SavedAccounts)
            {
                if (candidate.IsConnected && await _authManager.HasStoredTokenAsync(candidate.Id))
                {
                    fallbackAccount = await _driveClient.TryRestoreSessionAsync(candidate.Id, cancellationToken);
                    if (fallbackAccount is not null)
                    {
                        break;
                    }
                }
            }

            if (fallbackAccount is not null)
            {
                CurrentAccount = fallbackAccount;
                await _authManager.OrganizeAccountDirectoryAsync(fallbackAccount.Id, fallbackAccount.EmailAddress, fallbackAccount.DisplayName, cancellationToken);
                await _repository.UpsertAccountAsync(fallbackAccount, cancellationToken);
                await PersistActiveAccountIdAsync(fallbackAccount.Id, cancellationToken);
                var fallbackFolders = await _repository.GetSyncFoldersByAccountAsync(fallbackAccount.Id, cancellationToken);
                _watcherService.UpdateWatchers(fallbackFolders);
            }
            else
            {
                CurrentAccount = new DriveAccount
                {
                    Id = "default",
                    IsConnected = false
                };
                await PersistActiveAccountIdAsync("default", cancellationToken);
            }
        }

        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);
        AccountsListChanged?.Invoke(this, EventArgs.Empty);
        if (isDisconnectingActive)
        {
            AccountChanged?.Invoke(this, CurrentAccount);
            SyncFoldersChanged?.Invoke(this, EventArgs.Empty);
        }

        await LogAsync(ActivitySeverity.Info, "AccountDisconnect",
            string.IsNullOrWhiteSpace(disconnectedEmail)
                ? "Conta Google desconectada e tokens locais removidos."
                : $"Conta Google desconectada ({disconnectedEmail}) e tokens locais removidos.",
            null, null, cancellationToken);
    }

    public async Task RevokeAllAccountsAndCredentialsAsync(CancellationToken cancellationToken = default)
    {
        CancelAllActiveOperations();
        _watcherService.UpdateWatchers([]);

        var accounts = await _repository.GetAllAccountsAsync(cancellationToken);
        foreach (var account in accounts)
        {
            await _driveClient.DisconnectAsync(account.Id, cancellationToken);
            await _repository.DeleteAccountAsync(account.Id, cancellationToken);
        }

        if (_driveClient.IsConnected)
        {
            await _driveClient.DisconnectAsync(null, cancellationToken);
        }

        await _authManager.ClearAllLocalTokensAsync(cancellationToken);
        _authManager.DeleteCredentialsFile();

        CurrentAccount = new DriveAccount
        {
            Id = "default",
            IsConnected = false
        };
        await PersistActiveAccountIdAsync("default", cancellationToken);
        SavedAccounts = [];

        AccountsListChanged?.Invoke(this, EventArgs.Empty);
        AccountChanged?.Invoke(this, CurrentAccount);
        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

        await LogAsync(ActivitySeverity.Warning, "CredentialsRevoked",
            "Arquivo credentials.json e todos os dados/tokens de conta foram revogados.",
            null, _authManager.CredentialsFilePath, cancellationToken);
    }

    public async Task RevokeCredentialsFileAsync(CancellationToken cancellationToken = default)
    {
        _authManager.DeleteCredentialsFile();
        AccountsListChanged?.Invoke(this, EventArgs.Empty);

        await LogAsync(ActivitySeverity.Warning, "CredentialsJsonRevoked",
            "Arquivo credentials.json removido individualmente (contas conectadas preservadas).",
            null, _authManager.CredentialsFilePath, cancellationToken);
    }

    public async Task<DriveAccount?> RefreshAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!_driveClient.IsConnected)
        {
            return null;
        }

        var updated = await _driveClient.GetAccountInfoAsync(cancellationToken);
        CurrentAccount = updated;
        await _repository.UpsertAccountAsync(updated, cancellationToken);
        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);
        AccountsListChanged?.Invoke(this, EventArgs.Empty);
        AccountChanged?.Invoke(this, updated);
        return updated;
    }

    public Task<IReadOnlyList<LocalSyncFolder>> GetSyncFoldersAsync(CancellationToken cancellationToken = default) =>
        _driveClient.IsConnected
            ? _repository.GetSyncFoldersByAccountAsync(CurrentAccount.Id, cancellationToken)
            : Task.FromResult<IReadOnlyList<LocalSyncFolder>>([]);

    private void CancelAllActiveOperations()
    {
        foreach (var cts in _folderSyncCancellations.Values)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        }

        foreach (var cts in _transferCancellations.Values)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        }
    }

    private async Task PersistActiveAccountIdAsync(string accountId, CancellationToken cancellationToken)
    {
        if (string.Equals(_settingsService.Current.ActiveAccountId, accountId, StringComparison.Ordinal))
        {
            return;
        }

        var current = _settingsService.Current;
        current.ActiveAccountId = accountId;
        await _settingsService.SaveAsync(current, cancellationToken);
    }

    private async Task<IReadOnlyList<LocalSyncFolder>> RestoreAndSyncAccountFoldersAsync(
        DriveAccount account,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(account.EmailAddress))
        {
            await _repository.ReattachSyncFoldersByEmailAsync(account.Id, account.EmailAddress, cancellationToken);
        }

        var existingFolders = (await _repository.GetSyncFoldersByAccountAsync(account.Id, cancellationToken)).ToList();
        var knownPaths = existingFolders
            .Select(f => Path.GetFullPath(f.LocalPath))
            .ToHashSet(StringComparer.Ordinal);

        var profileFolders = await _authManager.LoadAccountSyncFoldersProfileAsync(account.Id, account.EmailAddress, cancellationToken);
        foreach (var saved in profileFolders)
        {
            if (string.IsNullOrWhiteSpace(saved.LocalPath))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(saved.LocalPath);
            if (knownPaths.Contains(fullPath))
            {
                continue;
            }

            Directory.CreateDirectory(fullPath);
            saved.Id = Guid.NewGuid().ToString("N");
            saved.AccountId = account.Id;
            saved.AccountEmail = account.EmailAddress?.Trim().ToLowerInvariant();
            saved.LocalPath = fullPath;
            await _repository.UpsertSyncFolderAsync(saved, cancellationToken);
            existingFolders.Add(saved);
            knownPaths.Add(fullPath);
        }

        var latest = await _repository.GetSyncFoldersByAccountAsync(account.Id, cancellationToken);
        await _authManager.SaveAccountSyncFoldersProfileAsync(account.Id, account.EmailAddress, latest, cancellationToken);
        return latest;
    }

    private async Task PersistCurrentAccountFoldersProfileAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CurrentAccount.Id) || string.IsNullOrWhiteSpace(CurrentAccount.EmailAddress))
        {
            return;
        }

        try
        {
            var folders = await _repository.GetSyncFoldersByAccountAsync(CurrentAccount.Id, cancellationToken);
            await _authManager.SaveAccountSyncFoldersProfileAsync(CurrentAccount.Id, CurrentAccount.EmailAddress, folders, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not update per-account sync folder profile.");
        }
    }

    public async Task<LocalSyncFolder> AddSyncFolderAsync(
        string localPath,
        string remoteFolderId,
        string remoteFolderName,
        SyncMode mode,
        CancellationToken cancellationToken = default)
    {
        var fullLocalPath = Path.GetFullPath(localPath);
        Directory.CreateDirectory(fullLocalPath);

        var folder = new LocalSyncFolder
        {
            Id = Guid.NewGuid().ToString("N"),
            AccountId = CurrentAccount.Id,
            AccountEmail = CurrentAccount.EmailAddress?.Trim().ToLowerInvariant(),
            LocalPath = fullLocalPath,
            RemoteFolderId = string.IsNullOrWhiteSpace(remoteFolderId) ? "root" : remoteFolderId,
            RemoteFolderName = string.IsNullOrWhiteSpace(remoteFolderName) ? "Meus Arquivos" : remoteFolderName,
            Mode = mode,
            State = FolderSyncState.Idle,
            IsPaused = false
        };

        await _repository.UpsertSyncFolderAsync(folder, cancellationToken);
        await PersistCurrentAccountFoldersProfileAsync(cancellationToken);
        _pendingInitialSyncFolders[folder.Id] = 1;
        var allFolders = await GetSyncFoldersAsync(cancellationToken);
        _watcherService.UpdateWatchers(allFolders);
        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

        await LogAsync(ActivitySeverity.Info, "FolderMapped",
            $"Pasta mapeada: {fullLocalPath} ↔ {folder.RemoteFolderName}.",
            folder.Id, fullLocalPath, cancellationToken);

        if (_driveClient.IsConnected)
        {
            _ = Task.Run(() => TriggerFolderSyncAsync(folder.Id), CancellationToken.None);
        }

        return folder;
    }

    public async Task UpdateFolderCustomizationAsync(
        string folderId,
        string? customAlias,
        string? customColorHex,
        string? customIconKey,
        CancellationToken cancellationToken = default)
    {
        var folderLock = _folderLocks.GetOrAdd(folderId, _ => new SemaphoreSlim(1, 1));
        await folderLock.WaitAsync(cancellationToken);
        LocalSyncFolder? folder;
        try
        {
            folder = await _repository.GetSyncFolderByIdAsync(folderId, cancellationToken);
            if (folder is null)
            {
                return;
            }

            folder.AccountEmail ??= CurrentAccount.EmailAddress?.Trim().ToLowerInvariant();
            folder.CustomAlias = string.IsNullOrWhiteSpace(customAlias) ? null : customAlias.Trim();
            folder.CustomColorHex = FolderCustomizationPresets.NormalizeColorHex(customColorHex);
            folder.CustomIconKey = string.IsNullOrWhiteSpace(customIconKey) ? null : customIconKey.Trim();

            await _repository.UpsertSyncFolderAsync(folder, cancellationToken);
            await PersistCurrentAccountFoldersProfileAsync(cancellationToken);
        }
        finally
        {
            folderLock.Release();
        }

        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

        await LogAsync(ActivitySeverity.Info, "FolderCustomized",
            $"Personalização salva para {folder.EffectiveDisplayName}.",
            folder.Id, folder.LocalPath, cancellationToken);
    }

    public async Task UpdateSyncModeAsync(
        string folderId,
        SyncMode mode,
        CancellationToken cancellationToken = default)
    {
        var folderLock = _folderLocks.GetOrAdd(folderId, _ => new SemaphoreSlim(1, 1));
        await folderLock.WaitAsync(cancellationToken);
        LocalSyncFolder? folder;
        try
        {
            folder = await _repository.GetSyncFolderByIdAsync(folderId, cancellationToken);
            if (folder is null)
            {
                return;
            }

            folder.AccountEmail ??= CurrentAccount.EmailAddress?.Trim().ToLowerInvariant();
            folder.Mode = mode;
            await _repository.UpsertSyncFolderAsync(folder, cancellationToken);
            await PersistCurrentAccountFoldersProfileAsync(cancellationToken);
        }
        finally
        {
            folderLock.Release();
        }

        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

        var modeLabel = mode == SyncMode.TwoWay ? "Sincronização Bidirecional" : "Backup Unidirecional";
        await LogAsync(ActivitySeverity.Info, "ModeChanged",
            $"Modo alterado para {modeLabel} em {folder.LocalPath}.",
            folder.Id, folder.LocalPath, cancellationToken);
    }

    public async Task SetFolderPausedAsync(
        string folderId,
        bool isPaused,
        CancellationToken cancellationToken = default)
    {
        if (isPaused && _folderSyncCancellations.TryGetValue(folderId, out var activeCts))
        {
            activeCts.Cancel();
        }

        var folderLock = _folderLocks.GetOrAdd(folderId, _ => new SemaphoreSlim(1, 1));
        await folderLock.WaitAsync(cancellationToken);
        LocalSyncFolder? folder;
        try
        {
            folder = await _repository.GetSyncFolderByIdAsync(folderId, cancellationToken);
            if (folder is null)
            {
                return;
            }

            folder.AccountEmail ??= CurrentAccount.EmailAddress?.Trim().ToLowerInvariant();
            folder.IsPaused = isPaused;
            folder.State = isPaused ? FolderSyncState.Paused : FolderSyncState.Idle;
            await _repository.UpsertSyncFolderAsync(folder, cancellationToken);
            await PersistCurrentAccountFoldersProfileAsync(cancellationToken);
        }
        finally
        {
            folderLock.Release();
        }

        if (isPaused)
        {
            foreach (var item in _transfers.Values)
            {
                if (string.Equals(item.SyncFolderId, folderId, StringComparison.Ordinal) &&
                    item.Status is TransferStatus.Queued or TransferStatus.InProgress)
                {
                    CancelTransfer(item.Id);
                }
            }
        }

        var folders = await GetSyncFoldersAsync(cancellationToken);
        _watcherService.UpdateWatchers(folders);
        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

        await LogAsync(ActivitySeverity.Info, isPaused ? "FolderPaused" : "FolderResumed",
            isPaused ? $"Sincronização pausada para {folder.LocalPath}." : $"Sincronização retomada para {folder.LocalPath}.",
            folder.Id, folder.LocalPath, cancellationToken);
    }

    public async Task RemoveSyncFolderAsync(string folderId, CancellationToken cancellationToken = default)
    {
        if (_folderSyncCancellations.TryGetValue(folderId, out var folderCts))
        {
            folderCts.Cancel();
        }

        LocalSyncFolder? folder;
        var folderLock = _folderLocks.GetOrAdd(folderId, _ => new SemaphoreSlim(1, 1));
        await folderLock.WaitAsync(cancellationToken);
        try
        {
            folder = await _repository.GetSyncFolderByIdAsync(folderId, cancellationToken);
            if (folder is null)
            {
                return;
            }

            _pendingInitialSyncFolders.TryRemove(folderId, out _);
            await _repository.DeleteSyncFolderAsync(folderId, cancellationToken);
        }
        finally
        {
            folderLock.Release();
        }
        await PersistCurrentAccountFoldersProfileAsync(cancellationToken);
        var folders = await GetSyncFoldersAsync(cancellationToken);
        _watcherService.UpdateWatchers(folders);
        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

        await LogAsync(ActivitySeverity.Warning, "FolderUnmapped",
            $"Mapeamento removido para {folder.LocalPath} (arquivos locais preservados).",
            null, folder.LocalPath, cancellationToken);
    }

    public async Task TriggerSyncAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_driveClient.IsConnected || !IsOnline)
        {
            if (!_driveClient.IsConnected)
            {
                await LogAsync(ActivitySeverity.Warning, "SyncSkipped",
                    "Sincronização ignorada: nenhuma conta Google conectada.", null, null, cancellationToken);
            }
            return;
        }

        var activeFolders = (await GetSyncFoldersAsync(cancellationToken))
            .Where(f => !f.IsPaused)
            .ToList();
        if (activeFolders.Count > 0)
        {
            var concurrencyLimit = _settingsService.Current.GetEffectiveTransferConcurrencyLimit();
            using var gate = concurrencyLimit is int limit ? new SemaphoreSlim(limit, limit) : null;
            var syncTasks = activeFolders.Select(async folder =>
            {
                if (gate is not null)
                {
                    await gate.WaitAsync(cancellationToken);
                }

                try
                {
                    await TriggerFolderSyncAsync(folder.Id, cancellationToken);
                }
                finally
                {
                    gate?.Release();
                }
            });
            await Task.WhenAll(syncTasks);
        }

        await RefreshAccountInfoAsync(cancellationToken);
    }

    public async Task TriggerFolderSyncAsync(string folderId, CancellationToken cancellationToken = default)
    {
        if (!_driveClient.IsConnected || !IsOnline)
        {
            return;
        }

        var folderLock = _folderLocks.GetOrAdd(folderId, _ => new SemaphoreSlim(1, 1));
        if (!await folderLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        using var folderCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _folderSyncCancellations[folderId] = folderCts;

        Interlocked.Increment(ref _activeSyncCount);
        var syncFailed = false;
        try
        {
            var folder = await _repository.GetSyncFolderByIdAsync(folderId, folderCts.Token);
            if (folder is null || folder.IsPaused)
            {
                return;
            }

            folder.State = FolderSyncState.Syncing;
            folder.LastErrorMessage = null;
            await _repository.UpsertSyncFolderAsync(folder, folderCts.Token);
            SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

            await LogAsync(ActivitySeverity.Info, "SyncStarted",
                $"Sincronização iniciada: {folder.LocalPath} ↔ {folder.RemoteFolderName}.",
                folder.Id, folder.LocalPath, folderCts.Token);

            var changesCount = await SyncFolderInternalAsync(folder, folderCts.Token);

            var latestFolder = await _repository.GetSyncFolderByIdAsync(folderId, CancellationToken.None) ?? folder;
            latestFolder.State = latestFolder.IsPaused ? FolderSyncState.Paused : FolderSyncState.Idle;
            latestFolder.LastSyncedAtUtc = DateTimeOffset.UtcNow;
            latestFolder.LastErrorMessage = null;
            await _repository.UpsertSyncFolderAsync(latestFolder, CancellationToken.None);

            var isInitialSync = _pendingInitialSyncFolders.TryRemove(folderId, out _);
            if (isInitialSync)
            {
                await LogAsync(ActivitySeverity.Success, "InitialFolderSyncCompleted",
                    $"Sincronização inicial concluída: {latestFolder.EffectiveDisplayName} ({latestFolder.TrackedFileCount} arquivo(s) sincronizado(s)).",
                    latestFolder.Id, latestFolder.LocalPath, CancellationToken.None);
            }
            else
            {
                var completionMessage = changesCount > 0
                    ? $"Sincronização concluída: {latestFolder.LocalPath} ({changesCount} alteração(ões))."
                    : $"Sincronização concluída: {latestFolder.LocalPath} (sem alterações).";
                await LogAsync(ActivitySeverity.Success, "SyncCompleted",
                    completionMessage,
                    latestFolder.Id, latestFolder.LocalPath, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            var latestFolder = await _repository.GetSyncFolderByIdAsync(folderId, CancellationToken.None);
            if (latestFolder is not null)
            {
                latestFolder.State = latestFolder.IsPaused ? FolderSyncState.Paused : FolderSyncState.Idle;
                await _repository.UpsertSyncFolderAsync(latestFolder, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            syncFailed = true;
            _logger.LogError(ex, "Sync failed for folder '{FolderId}'.", folderId);
            var folder = await _repository.GetSyncFolderByIdAsync(folderId, CancellationToken.None);
            if (folder is not null)
            {
                folder.State = folder.IsPaused ? FolderSyncState.Paused : FolderSyncState.Error;
                folder.LastErrorMessage = ex.Message;
                await _repository.UpsertSyncFolderAsync(folder, CancellationToken.None);
            }

            await LogAsync(ActivitySeverity.Error, "SyncError",
                $"Erro ao sincronizar pasta: {ex.Message}", folderId, folder?.LocalPath, CancellationToken.None);
        }
        finally
        {
            foreach (var stuck in _transfers.Values)
            {
                if (string.Equals(stuck.SyncFolderId, folderId, StringComparison.Ordinal) &&
                    stuck.Status is TransferStatus.Queued or TransferStatus.InProgress)
                {
                    stuck.Status = syncFailed ? TransferStatus.Failed : TransferStatus.Cancelled;
                    stuck.CompletedAtUtc = DateTimeOffset.UtcNow;
                    if (syncFailed && string.IsNullOrWhiteSpace(stuck.ErrorMessage))
                    {
                        stuck.ErrorMessage = "Sync ended with error.";
                    }
                    PublishTransfer(stuck);
                }
            }

            _folderSyncCancellations.TryRemove(folderId, out _);
            Interlocked.Decrement(ref _activeSyncCount);
            folderLock.Release();
            SyncFoldersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task<RemoteDriveNode> UploadSingleFileAsync(
        string localFilePath,
        string remoteFolderId = "root",
        CancellationToken cancellationToken = default) =>
        UploadSingleFileCoreAsync(localFilePath, remoteFolderId, existingRemoteFileId: null, existingQueueItem: null, cancellationToken);

    private async Task<RemoteDriveNode> UploadSingleFileCoreAsync(
        string localFilePath,
        string remoteFolderId,
        string? existingRemoteFileId,
        TransferQueueItem? existingQueueItem,
        CancellationToken cancellationToken)
    {
        if (!_driveClient.IsConnected)
        {
            throw new InvalidOperationException("Conta Google Drive não conectada.");
        }

        var fullPath = Path.GetFullPath(localFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Arquivo local não encontrado.", fullPath);
        }

        var fileInfo = new FileInfo(fullPath);
        var normalizedParent = string.IsNullOrWhiteSpace(remoteFolderId) ? "root" : remoteFolderId;
        var queueItem = existingQueueItem ?? CreateTransferItem(
            syncFolderId: "api-upload",
            fileName: fileInfo.Name,
            relativePath: fileInfo.Name,
            localPath: fullPath,
            remoteFileId: existingRemoteFileId,
            direction: TransferDirection.Upload,
            totalBytes: fileInfo.Length);
        queueItem.RemoteParentId = normalizedParent;
        queueItem.TotalBytes = fileInfo.Length;
        queueItem.ErrorMessage = null;
        queueItem.CompletedAtUtc = null;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _transferCancellations[queueItem.Id] = linkedCts;

        try
        {
            queueItem.Status = TransferStatus.InProgress;
            PublishTransfer(queueItem);

            var uploaded = await UploadFileWithResumeAsync(
                syncFolderId: queueItem.SyncFolderId,
                fileInfo: fileInfo,
                parentFolderId: normalizedParent,
                existingRemoteFileId: existingRemoteFileId,
                queueItem: queueItem,
                cancellationToken: linkedCts.Token);

            queueItem.RemoteFileId = uploaded.Id;
            queueItem.TransferredBytes = queueItem.TotalBytes;
            queueItem.Status = TransferStatus.Completed;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);

            await LogAsync(ActivitySeverity.Success, "ApiUpload",
                $"Arquivo enviado: {fileInfo.Name}.",
                null, fullPath, cancellationToken);

            return uploaded;
        }
        catch (OperationCanceledException)
        {
            queueItem.Status = TransferStatus.Cancelled;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            throw;
        }
        catch (Exception ex)
        {
            queueItem.Status = TransferStatus.Failed;
            queueItem.ErrorMessage = ex.Message;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            await LogAsync(ActivitySeverity.Error, "ApiUploadFailed",
                $"Falha no envio ({fileInfo.Name}): {ex.Message}",
                null, fullPath, CancellationToken.None);
            throw;
        }
        finally
        {
            _transferCancellations.TryRemove(queueItem.Id, out _);
        }
    }

    public async Task TrashRemoteItemAsync(string remoteFileId, string itemName, CancellationToken cancellationToken = default)
    {
        var queueItem = CreateTransferItem("remote-trash", itemName, itemName, itemName, remoteFileId, TransferDirection.Delete, 0);
        queueItem.Status = TransferStatus.InProgress;
        PublishTransfer(queueItem);

        try
        {
            await _driveClient.TrashFileAsync(remoteFileId, cancellationToken);
            queueItem.Status = TransferStatus.Completed;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);

            await LogAsync(ActivitySeverity.Warning, "RemoteTrashed",
                $"Movido para a Lixeira do Google Drive ({itemName}).",
                null, itemName, cancellationToken);
        }
        catch (Exception ex)
        {
            queueItem.Status = TransferStatus.Failed;
            queueItem.ErrorMessage = ex.Message;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            throw;
        }
    }

    public async Task RestoreRemoteItemAsync(string remoteFileId, string itemName, CancellationToken cancellationToken = default)
    {
        await _driveClient.RestoreFileAsync(remoteFileId, cancellationToken);
        await LogAsync(ActivitySeverity.Success, "RemoteRestored",
            $"Item restaurado da Lixeira do Google Drive ({itemName}).",
            null, itemName, cancellationToken);
    }

    public async Task DeleteRemoteItemPermanentlyAsync(string remoteFileId, string itemName, CancellationToken cancellationToken = default)
    {
        var queueItem = CreateTransferItem("remote-delete", itemName, itemName, itemName, remoteFileId, TransferDirection.Delete, 0);
        queueItem.Status = TransferStatus.InProgress;
        PublishTransfer(queueItem);

        try
        {
            await _driveClient.DeletePermanentlyAsync(remoteFileId, cancellationToken);
            queueItem.Status = TransferStatus.Completed;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);

            await LogAsync(ActivitySeverity.Warning, "RemoteDeletedPermanent",
                $"Excluído permanentemente do Google Drive ({itemName}).",
                null, itemName, cancellationToken);
        }
        catch (Exception ex)
        {
            queueItem.Status = TransferStatus.Failed;
            queueItem.ErrorMessage = ex.Message;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            throw;
        }
    }

    public async Task EmptyRemoteTrashAsync(CancellationToken cancellationToken = default)
    {
        var queueItem = CreateTransferItem("empty-trash", "Lixeira do Google Drive", "Lixeira", "Lixeira", null, TransferDirection.Delete, 0);
        queueItem.Status = TransferStatus.InProgress;
        PublishTransfer(queueItem);

        try
        {
            await _driveClient.EmptyTrashAsync(cancellationToken);
            queueItem.Status = TransferStatus.Completed;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);

            await LogAsync(ActivitySeverity.Warning, "TrashEmptied",
                "Lixeira do Google Drive esvaziada permanentemente.",
                null, null, cancellationToken);
        }
        catch (Exception ex)
        {
            queueItem.Status = TransferStatus.Failed;
            queueItem.ErrorMessage = ex.Message;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            throw;
        }
    }

    public Task<string> DownloadRemoteFileAsync(
        string remoteFileId,
        string fileName,
        long sizeBytes = 0,
        bool isGoogleWorkspaceDoc = false,
        string? mimeType = null,
        CancellationToken cancellationToken = default)
    {
        var downloadDir = _settingsService.Current.DownloadDirectory;
        if (string.IsNullOrWhiteSpace(downloadDir))
        {
            downloadDir = AppPaths.DefaultDownloadDirectory;
        }

        return DownloadRemoteFileCoreAsync(
            remoteFileId,
            fileName,
            downloadDir,
            treatAsFullFilePath: false,
            sizeBytes,
            isGoogleWorkspaceDoc,
            mimeType,
            existingQueueItem: null,
            cancellationToken);
    }

    public Task<string> DownloadRemoteFileToAsync(
        string remoteFileId,
        string fileName,
        string destinationPathOrDirectory,
        bool treatAsFullFilePath = false,
        long sizeBytes = 0,
        bool isGoogleWorkspaceDoc = false,
        string? mimeType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPathOrDirectory))
        {
            return DownloadRemoteFileAsync(remoteFileId, fileName, sizeBytes, isGoogleWorkspaceDoc, mimeType, cancellationToken);
        }

        return DownloadRemoteFileCoreAsync(
            remoteFileId,
            fileName,
            destinationPathOrDirectory,
            treatAsFullFilePath,
            sizeBytes,
            isGoogleWorkspaceDoc,
            mimeType,
            existingQueueItem: null,
            cancellationToken);
    }

    private async Task<string> DownloadRemoteFileCoreAsync(
        string remoteFileId,
        string fileName,
        string destinationPathOrDirectory,
        bool treatAsFullFilePath,
        long sizeBytes,
        bool isGoogleWorkspaceDoc,
        string? mimeType,
        TransferQueueItem? existingQueueItem,
        CancellationToken cancellationToken)
    {
        if (!_driveClient.IsConnected)
        {
            throw new InvalidOperationException("Conta Google Drive não conectada.");
        }

        var safeName = string.IsNullOrWhiteSpace(fileName)
            ? $"arquivo_{remoteFileId}"
            : fileName.Replace('/', '_');

        string? exportMime = null;
        if (isGoogleWorkspaceDoc)
        {
            (exportMime, var ext) = mimeType switch
            {
                "application/vnd.google-apps.document" => ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx"),
                "application/vnd.google-apps.spreadsheet" => ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx"),
                "application/vnd.google-apps.presentation" => ("application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx"),
                "application/vnd.google-apps.drawing" => ("image/png", ".png"),
                "application/vnd.google-apps.vid" => ((string?)null, ".mp4"),
                _ => ("application/pdf", ".pdf")
            };

            if (!safeName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                safeName += ext;
            }
        }

        string targetPath;
        if (treatAsFullFilePath)
        {
            targetPath = Path.GetFullPath(destinationPathOrDirectory);
            var parentDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }
        }
        else
        {
            Directory.CreateDirectory(destinationPathOrDirectory);
            lock (_downloadPathLock)
            {
                targetPath = GetUniqueDestinationPath(destinationPathOrDirectory, safeName, _reservedDownloadPaths);
                _reservedDownloadPaths.Add(targetPath);
            }
        }

        var queueItem = existingQueueItem ?? CreateTransferItem(
            syncFolderId: "manual-download",
            fileName: Path.GetFileName(targetPath),
            relativePath: Path.GetFileName(targetPath),
            localPath: targetPath,
            remoteFileId: remoteFileId,
            direction: TransferDirection.Download,
            totalBytes: sizeBytes);
        queueItem.IsGoogleWorkspaceDoc = isGoogleWorkspaceDoc;
        queueItem.MimeType = mimeType;
        queueItem.ErrorMessage = null;
        queueItem.CompletedAtUtc = null;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _transferCancellations[queueItem.Id] = linkedCts;

        try
        {
            queueItem.Status = TransferStatus.InProgress;
            PublishTransfer(queueItem);

            if (exportMime is not null)
            {
                await _driveClient.ExportFileAsync(
                    remoteFileId,
                    exportMime,
                    targetPath,
                    bytes =>
                    {
                        queueItem.TransferredBytes = bytes;
                        PublishTransfer(queueItem);
                    },
                    linkedCts.Token);
            }
            else
            {
                await DownloadFileWithResumeAsync(
                    remoteFileId: remoteFileId,
                    targetLocalPath: targetPath,
                    knownTotalBytes: sizeBytes,
                    queueItem: queueItem,
                    cancellationToken: linkedCts.Token);
            }

            if (File.Exists(targetPath))
            {
                var actualLen = new FileInfo(targetPath).Length;
                queueItem.TotalBytes = Math.Max(queueItem.TotalBytes, actualLen);
                queueItem.TransferredBytes = queueItem.TotalBytes;
            }

            queueItem.Status = TransferStatus.Completed;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);

            await LogAsync(ActivitySeverity.Success, "ManualDownload",
                $"Baixado para {targetPath}.",
                null, targetPath, cancellationToken);

            return targetPath;
        }
        catch (OperationCanceledException)
        {
            queueItem.Status = TransferStatus.Cancelled;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            throw;
        }
        catch (Exception ex)
        {
            queueItem.Status = TransferStatus.Failed;
            queueItem.ErrorMessage = ex.Message;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            await LogAsync(ActivitySeverity.Error, "ManualDownloadFailed",
                $"Falha ao baixar {safeName}: {ex.Message}",
                null, targetPath, CancellationToken.None);
            throw;
        }
        finally
        {
            if (!treatAsFullFilePath)
            {
                lock (_downloadPathLock)
                {
                    _reservedDownloadPaths.Remove(targetPath);
                }
            }
            _transferCancellations.TryRemove(queueItem.Id, out _);
        }
    }

    public async Task<IReadOnlyList<DrivePermissionEntry>> GetRemotePermissionsAsync(
        string remoteFileId,
        CancellationToken cancellationToken = default) =>
        await _driveClient.ListPermissionsAsync(remoteFileId, cancellationToken);

    public async Task<string> EnableAnyoneLinkAsync(
        string remoteFileId,
        string itemName,
        bool canEdit,
        CancellationToken cancellationToken = default)
    {
        var role = canEdit ? "writer" : "reader";
        await _driveClient.ShareAnyoneAsync(remoteFileId, role, cancellationToken);
        var link = await _driveClient.GetShareLinkAsync(remoteFileId, cancellationToken)
            ?? $"https://drive.google.com/file/d/{Uri.EscapeDataString(remoteFileId)}/view";

        await LogAsync(ActivitySeverity.Success, "ShareLinkEnabled",
            $"Link compartilhável ({(canEdit ? "edição" : "leitura")}) ativado para {itemName}.",
            null, itemName, cancellationToken);
        return link;
    }

    public async Task DisableAnyoneLinkAsync(
        string remoteFileId,
        string itemName,
        CancellationToken cancellationToken = default)
    {
        var permissions = await _driveClient.ListPermissionsAsync(remoteFileId, cancellationToken);
        var anyone = permissions.FirstOrDefault(p => string.Equals(p.Type, "anyone", StringComparison.OrdinalIgnoreCase));
        if (anyone is not null)
        {
            await _driveClient.RevokePermissionAsync(remoteFileId, anyone.Id, cancellationToken);
            await LogAsync(ActivitySeverity.Info, "ShareLinkRevoked",
                $"Link compartilhável revogado para {itemName}.",
                null, itemName, cancellationToken);
        }
    }

    public async Task<DrivePermissionEntry> ShareWithEmailAsync(
        string remoteFileId,
        string itemName,
        string emailAddress,
        bool canEdit,
        CancellationToken cancellationToken = default)
    {
        var role = canEdit ? "writer" : "reader";
        var perm = await _driveClient.InviteUserPermissionAsync(remoteFileId, emailAddress, role, true, cancellationToken);
        await LogAsync(ActivitySeverity.Success, "SharedWithUser",
            $"Compartilhado ({(canEdit ? "edição" : "leitura")}) com {emailAddress.Trim()}: {itemName}.",
            null, itemName, cancellationToken);
        return perm;
    }

    public async Task RevokeRemotePermissionAsync(
        string remoteFileId,
        string itemName,
        string permissionId,
        CancellationToken cancellationToken = default)
    {
        await _driveClient.RevokePermissionAsync(remoteFileId, permissionId, cancellationToken);
        await LogAsync(ActivitySeverity.Info, "PermissionRevoked",
            $"Permissão removida de {itemName}.",
            null, itemName, cancellationToken);
    }

    public async Task<bool> RetryTransferAsync(string transferId, CancellationToken cancellationToken = default)
    {
        if (!_transfers.TryGetValue(transferId, out var item) || !item.CanRetry)
        {
            return false;
        }

        if (!_driveClient.IsConnected || !IsOnline)
        {
            item.Status = TransferStatus.Failed;
            item.ErrorMessage = !_driveClient.IsConnected
                ? "Conta Google Drive não conectada."
                : "Sem conexão com a internet no momento.";
            item.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(item);
            return false;
        }

        if (item.Direction == TransferDirection.Download)
        {
            if (string.IsNullOrWhiteSpace(item.RemoteFileId))
            {
                item.Status = TransferStatus.Failed;
                item.ErrorMessage = "Arquivo remoto não identificado para retomada.";
                item.CompletedAtUtc = DateTimeOffset.UtcNow;
                PublishTransfer(item);
                return false;
            }

            await DownloadRemoteFileCoreAsync(
                item.RemoteFileId,
                item.FileName,
                item.LocalPath,
                treatAsFullFilePath: true,
                item.TotalBytes,
                item.IsGoogleWorkspaceDoc,
                item.MimeType,
                existingQueueItem: item,
                cancellationToken);

            // Sync tracked metadata when retrying a mapped-folder download.
            if (!string.IsNullOrWhiteSpace(item.SyncFolderId) &&
                item.SyncFolderId != "api-download" &&
                File.Exists(item.LocalPath))
            {
                var syncFolder = await _repository.GetSyncFolderByIdAsync(item.SyncFolderId, CancellationToken.None);
                var remoteNode = await _driveClient.GetFileMetadataAsync(item.RemoteFileId, CancellationToken.None);
                if (syncFolder is not null && remoteNode is not null)
                {
                    var localInfo = new FileInfo(item.LocalPath);
                    var hashes = await FileHashUtility.ComputeHashesAsync(localInfo.FullName, CancellationToken.None);
                    var localModUtc = new DateTimeOffset(localInfo.LastWriteTimeUtc, TimeSpan.Zero);
                    await UpsertTrackedAsync(syncFolder.Id, item.RelativePath, remoteNode, hashes, localInfo.Length, localModUtc, CancellationToken.None);
                }
            }

            return true;
        }

        if (item.Direction == TransferDirection.Upload)
        {
            if (string.IsNullOrWhiteSpace(item.LocalPath) || !File.Exists(item.LocalPath))
            {
                item.Status = TransferStatus.Failed;
                item.ErrorMessage = "Arquivo local não encontrado.";
                item.CompletedAtUtc = DateTimeOffset.UtcNow;
                PublishTransfer(item);
                return false;
            }

            var folder = !string.IsNullOrWhiteSpace(item.SyncFolderId) && item.SyncFolderId != "api-upload"
                ? await _repository.GetSyncFolderByIdAsync(item.SyncFolderId, cancellationToken)
                : null;

            if (folder is not null)
            {
                item.Status = TransferStatus.InProgress;
                item.ErrorMessage = null;
                item.CompletedAtUtc = null;
                PublishTransfer(item);

                try
                {
                    var localFile = new FileInfo(item.LocalPath);
                    item.TotalBytes = localFile.Length;
                    var parentId = !string.IsNullOrWhiteSpace(item.RemoteParentId)
                        ? item.RemoteParentId
                        : await ResolveOrCreateRemoteParentFolderAsync(folder.RemoteFolderId, item.RelativePath, cancellationToken);
                    var hashes = await FileHashUtility.ComputeHashesAsync(localFile.FullName, cancellationToken);
                    await UploadAndTrackAsync(
                        folder,
                        item.RelativePath,
                        localFile,
                        parentId,
                        item.RemoteFileId,
                        hashes,
                        cancellationToken,
                        existingQueueItem: item);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    if (item.Status != TransferStatus.Cancelled)
                    {
                        item.Status = TransferStatus.Cancelled;
                        item.CompletedAtUtc = DateTimeOffset.UtcNow;
                        PublishTransfer(item);
                    }
                    throw;
                }
                catch (Exception ex)
                {
                    if (item.Status != TransferStatus.Failed)
                    {
                        item.Status = TransferStatus.Failed;
                        item.ErrorMessage = ex.Message;
                        item.CompletedAtUtc = DateTimeOffset.UtcNow;
                        PublishTransfer(item);
                    }
                    throw;
                }
            }

            await UploadSingleFileCoreAsync(
                item.LocalPath,
                item.RemoteParentId ?? "root",
                item.RemoteFileId,
                existingQueueItem: item,
                cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<string> ResolveOrCreateRemoteParentFolderAsync(
        string rootRemoteFolderId,
        string relativeFilePath,
        CancellationToken cancellationToken)
    {
        var dirPart = Path.GetDirectoryName(relativeFilePath)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(dirPart) || dirPart == ".")
        {
            return rootRemoteFolderId;
        }

        var currentParentId = rootRemoteFolderId;
        foreach (var segment in dirPart.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var folderNode = await _driveClient.FindOrCreateFolderAsync(segment, currentParentId, cancellationToken);
            currentParentId = folderNode.Id;
        }

        return currentParentId;
    }

    public async Task<int> RetryAllFailedTransfersAsync(CancellationToken cancellationToken = default)
    {
        var retryIds = _transfers.Values
            .Where(t => t.Status == TransferStatus.Failed && t.CanRetry)
            .Select(t => t.Id)
            .ToList();

        var retried = 0;
        foreach (var id in retryIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (await RetryTransferAsync(id, cancellationToken))
                {
                    retried++;
                }
            }
            catch
            {
                // Individual retry failure stays visible on that transfer item.
            }
        }

        return retried;
    }

    private long GetBandwidthLimitBytesPerSecond() =>
        _settingsService.Current.GetEffectiveBandwidthLimitBytesPerSecond();

    private async Task<RemoteDriveNode> UploadFileWithResumeAsync(
        string syncFolderId,
        FileInfo fileInfo,
        string parentFolderId,
        string? existingRemoteFileId,
        TransferQueueItem queueItem,
        CancellationToken cancellationToken)
    {
        if (_driveClient is not IResumableDriveClient resumableClient)
        {
            return await _driveClient.UploadFileAsync(
                fileInfo.FullName,
                parentFolderId,
                existingRemoteFileId,
                bytes =>
                {
                    queueItem.TransferredBytes = bytes;
                    PublishTransfer(queueItem);
                },
                cancellationToken);
        }

        var resumeRepo = _repository as ITransferResumeRepository;
        var transferKey = $"upload:{syncFolderId}:{fileInfo.FullName}";
        var localMtimeTicks = fileInfo.LastWriteTimeUtc.Ticks;
        var existingResume = resumeRepo is not null
            ? await resumeRepo.GetResumeSessionAsync(transferKey, CancellationToken.None)
            : null;
        string? resumeUri = null;
        if (existingResume is not null &&
            existingResume.LocalMtimeTicks == localMtimeTicks &&
            existingResume.TotalBytes == fileInfo.Length &&
            !string.IsNullOrWhiteSpace(existingResume.UploadSessionUri))
        {
            resumeUri = existingResume.UploadSessionUri;
        }

        string? activeUri = resumeUri;
        long lastPersistedBytes = existingResume?.TransferredBytes ?? 0;

        async Task PersistUploadStateAsync(string? sessionUri, long bytes)
        {
            if (resumeRepo is null || string.IsNullOrWhiteSpace(sessionUri))
            {
                return;
            }

            await resumeRepo.UpsertResumeSessionAsync(new TransferResumeSession
            {
                TransferKey = transferKey,
                Direction = TransferDirection.Upload,
                LocalPath = fileInfo.FullName,
                RemoteFileId = existingRemoteFileId,
                RemoteParentId = parentFolderId,
                UploadSessionUri = sessionUri,
                TotalBytes = fileInfo.Length,
                TransferredBytes = bytes,
                LocalMtimeTicks = localMtimeTicks,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }

        try
        {
            var uploaded = await resumableClient.ResumableUploadAsync(
                fileInfo.FullName,
                parentFolderId,
                existingRemoteFileId,
                resumeUri,
                onSessionCreated: async uri =>
                {
                    activeUri = uri;
                    await PersistUploadStateAsync(uri, 0);
                },
                onBytesTransferred: bytes =>
                {
                    queueItem.TransferredBytes = bytes;
                    PublishTransfer(queueItem);
                    if (!string.IsNullOrWhiteSpace(activeUri) && bytes - lastPersistedBytes >= 1024 * 1024)
                    {
                        lastPersistedBytes = bytes;
                        _ = PersistUploadStateAsync(activeUri, bytes);
                    }
                },
                getBandwidthLimitBytesPerSecond: GetBandwidthLimitBytesPerSecond,
                bandwidthLimiter: _bandwidthLimiter,
                cancellationToken: cancellationToken);

            if (resumeRepo is not null)
            {
                await resumeRepo.DeleteResumeSessionAsync(transferKey, CancellationToken.None);
            }
            return uploaded;
        }
        catch
        {
            await PersistUploadStateAsync(activeUri, queueItem.TransferredBytes);
            throw;
        }
    }

    private async Task DownloadFileWithResumeAsync(
        string remoteFileId,
        string targetLocalPath,
        long knownTotalBytes,
        TransferQueueItem queueItem,
        CancellationToken cancellationToken)
    {
        if (_driveClient is not IResumableDriveClient resumableClient)
        {
            await _driveClient.DownloadFileAsync(
                remoteFileId,
                targetLocalPath,
                bytes =>
                {
                    queueItem.TransferredBytes = bytes;
                    PublishTransfer(queueItem);
                },
                cancellationToken);
            return;
        }

        var resumeRepo = _repository as ITransferResumeRepository;
        var transferKey = $"download:{remoteFileId}:{targetLocalPath}";
        var existingResume = resumeRepo is not null
            ? await resumeRepo.GetResumeSessionAsync(transferKey, CancellationToken.None)
            : null;
        var activeTempPath = existingResume?.TempFilePath;
        long lastPersistedBytes = existingResume?.TransferredBytes ?? 0;

        async Task PersistDownloadStateAsync(string? tempPath, long bytes)
        {
            if (resumeRepo is null || string.IsNullOrWhiteSpace(tempPath))
            {
                return;
            }

            await resumeRepo.UpsertResumeSessionAsync(new TransferResumeSession
            {
                TransferKey = transferKey,
                Direction = TransferDirection.Download,
                LocalPath = targetLocalPath,
                RemoteFileId = remoteFileId,
                TempFilePath = tempPath,
                TotalBytes = knownTotalBytes,
                TransferredBytes = bytes,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }

        try
        {
            await resumableClient.ResumableDownloadAsync(
                remoteFileId,
                targetLocalPath,
                resumeTempPath: activeTempPath,
                knownTotalBytes: knownTotalBytes,
                onTempPathAssigned: async tempPath =>
                {
                    activeTempPath = tempPath;
                    await PersistDownloadStateAsync(tempPath, 0);
                },
                onBytesTransferred: bytes =>
                {
                    queueItem.TransferredBytes = bytes;
                    PublishTransfer(queueItem);
                    if (!string.IsNullOrWhiteSpace(activeTempPath) && bytes - lastPersistedBytes >= 1024 * 1024)
                    {
                        lastPersistedBytes = bytes;
                        _ = PersistDownloadStateAsync(activeTempPath, bytes);
                    }
                },
                getBandwidthLimitBytesPerSecond: GetBandwidthLimitBytesPerSecond,
                bandwidthLimiter: _bandwidthLimiter,
                keepTempOnInterrupt: true,
                cancellationToken: cancellationToken);

            if (resumeRepo is not null)
            {
                await resumeRepo.DeleteResumeSessionAsync(transferKey, CancellationToken.None);
            }
        }
        catch
        {
            await PersistDownloadStateAsync(activeTempPath, queueItem.TransferredBytes);
            throw;
        }
    }

    public static string SanitizeRemoteSegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "item";
        }

        var cleaned = name
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace('\0', '_');

        if (cleaned is "." or "..")
        {
            return "_";
        }

        return cleaned;
    }

    public static string ResolveSafeLocalPath(string rootDirectory, string relativePath)
    {
        var fullRoot = Path.GetFullPath(rootDirectory);
        var rootWithSep = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!combined.StartsWith(rootWithSep, comparison))
        {
            throw new UnauthorizedAccessException($"Path traversal blocked for '{relativePath}'.");
        }

        return combined;
    }

    private static string GetUniqueDestinationPath(
        string directory,
        string fileName,
        HashSet<string>? reservedPaths = null)
    {
        var safeFileName = SanitizeRemoteSegment(fileName);
        var candidate = ResolveSafeLocalPath(directory, safeFileName);
        if (!File.Exists(candidate) && (reservedPaths is null || !reservedPaths.Contains(candidate)))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var ext = Path.GetExtension(safeFileName);
        var counter = 1;
        while (File.Exists(candidate) || (reservedPaths is not null && reservedPaths.Contains(candidate)))
        {
            candidate = ResolveSafeLocalPath(directory, $"{baseName} ({counter}){ext}");
            counter++;
        }

        return candidate;
    }

    public IReadOnlyList<TransferQueueItem> GetTransferQueueSnapshot() =>
        _transfers.Values
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(CloneTransfer)
            .ToList();

    public TransferQueueStats GetTransferQueueStats()
    {
        var active = 0;
        var completed = Volatile.Read(ref _prunedCompletedCount);
        var failed = Volatile.Read(ref _prunedFailedCount);
        var cancelled = Volatile.Read(ref _prunedCancelledCount);

        foreach (var item in _transfers.Values)
        {
            switch (item.Status)
            {
                case TransferStatus.Queued:
                case TransferStatus.InProgress:
                    active++;
                    break;
                case TransferStatus.Completed:
                    completed++;
                    break;
                case TransferStatus.Failed:
                    failed++;
                    break;
                case TransferStatus.Cancelled:
                    cancelled++;
                    break;
            }
        }

        return new TransferQueueStats(active, completed, failed, cancelled, active + completed + failed + cancelled);
    }

    public void CancelTransfer(string transferId)
    {
        if (_transferCancellations.TryGetValue(transferId, out var cts))
        {
            cts.Cancel();
        }
    }

    public void ClearCompletedTransfers()
    {
        foreach (var (id, item) in _transfers)
        {
            if (item.Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Cancelled)
            {
                _transfers.TryRemove(id, out _);
            }
        }

        Interlocked.Exchange(ref _prunedCompletedCount, 0);
        Interlocked.Exchange(ref _prunedFailedCount, 0);
        Interlocked.Exchange(ref _prunedCancelledCount, 0);
    }

    private async Task<int> SyncFolderInternalAsync(LocalSyncFolder folder, CancellationToken cancellationToken)
    {
        var changesCount = 0;
        var trackedAll = await _repository.GetTrackedFilesByFolderAsync(folder.Id, cancellationToken);
        var trackedFiles = new Dictionary<string, TrackedFileEntry>(StringComparer.Ordinal);
        var trackedFolders = new Dictionary<string, TrackedFileEntry>(StringComparer.Ordinal);

        foreach (var entry in trackedAll)
        {
            if (entry.IsFolder)
            {
                trackedFolders[entry.RelativePath] = entry;
            }
            else
            {
                trackedFiles[entry.RelativePath] = entry;
            }
        }

        if (!Directory.Exists(folder.LocalPath))
        {
            if (trackedFiles.Count > 0 || trackedFolders.Count > 0)
            {
                throw new DirectoryNotFoundException(
                    $"Pasta local '{folder.LocalPath}' não foi encontrada ou está desmontada. Sincronização abortada para proteger seus arquivos no Google Drive.");
            }

            Directory.CreateDirectory(folder.LocalPath);
        }

        var ignorePatterns = _settingsService.Current.IgnorePatterns;

        var localFiles = ScanLocalFiles(folder.LocalPath, ignorePatterns);
        var localDirs = ScanLocalDirectories(folder.LocalPath, ignorePatterns);
        var localDirSet = new HashSet<string>(localDirs, StringComparer.Ordinal);
        var remoteTree = await ScanRemoteTreeAsync(folder.RemoteFolderId, cancellationToken);

        var remoteFoldersByRelPath = remoteTree.FoldersByRelativePath;
        var remoteFilesByRelPath = remoteTree.FilesByRelativePath;
        var trashedAny = false;

        // Collect all previously synced folder paths (explicit folder rows + ancestor paths of tracked files).
        var previouslySyncedDirs = new HashSet<string>(trackedFolders.Keys, StringComparer.Ordinal);
        foreach (var fileRelPath in trackedFiles.Keys)
        {
            foreach (var ancestorDir in EnumerateAncestorDirectories(fileRelPath))
            {
                previouslySyncedDirs.Add(ancestorDir);
            }
        }

        // 1. Whole-folder deletion: folder deleted remotely on Drive -> trash entire local folder once.
        var remoteDeletedDirs = FilterTopMostFolders(previouslySyncedDirs.Where(relDir =>
            !remoteFoldersByRelPath.ContainsKey(relDir) &&
            Directory.Exists(ResolveSafeLocalPath(folder.LocalPath, relDir))));

        if (remoteDeletedDirs.Count > 0)
        {
            if (folder.Mode == SyncMode.TwoWay)
            {
                foreach (var relDir in remoteDeletedDirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var localDirPath = ResolveSafeLocalPath(folder.LocalPath, relDir);
                    var dirPrefix = relDir + "/";
                    var totalBytes = localFiles
                        .Where(kv => kv.Key.StartsWith(dirPrefix, StringComparison.Ordinal))
                        .Sum(kv => kv.Value.Length);
                    trackedFolders.TryGetValue(relDir, out var trackedDir);

                    var deleteQueueItem = CreateTransferItem(
                        folder.Id,
                        Path.GetFileName(relDir),
                        relDir,
                        localDirPath,
                        trackedDir?.RemoteFileId,
                        TransferDirection.Delete,
                        totalBytes);
                    deleteQueueItem.Status = TransferStatus.InProgress;
                    PublishTransfer(deleteQueueItem);

                    try
                    {
                        SuppressWatcherForFolder(folder.Id);
                        try
                        {
                            MoveLocalDirectoryToSystemTrash(localDirPath);
                        }
                        finally
                        {
                            ReleaseWatcherSuppressionDelayed(folder.Id);
                        }

                        deleteQueueItem.TransferredBytes = totalBytes;
                        deleteQueueItem.Status = TransferStatus.Completed;
                        deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                        PublishTransfer(deleteQueueItem);
                    }
                    catch (OperationCanceledException)
                    {
                        deleteQueueItem.Status = TransferStatus.Cancelled;
                        deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                        PublishTransfer(deleteQueueItem);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        deleteQueueItem.Status = TransferStatus.Failed;
                        deleteQueueItem.ErrorMessage = ex.Message;
                        deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                        PublishTransfer(deleteQueueItem);
                        throw;
                    }

                    await RemoveTrackedSubtreeAsync(relDir, trackedFolders, trackedFiles, cancellationToken);
                    RemoveSubtreeFromMap(localFiles, relDir);
                    localDirs.RemoveAll(d => string.Equals(d, relDir, StringComparison.Ordinal) || d.StartsWith(dirPrefix, StringComparison.Ordinal));
                    localDirSet.RemoveWhere(d => string.Equals(d, relDir, StringComparison.Ordinal) || d.StartsWith(dirPrefix, StringComparison.Ordinal));
                    changesCount++;

                    await LogAsync(ActivitySeverity.Info, "LocalTrashed",
                        $"Pasta movida para lixeira local após exclusão remota: {relDir}.",
                        folder.Id, relDir, cancellationToken);
                }
            }
            else
            {
                // In OneWayBackup, keep local directories intact and clear stale remote folder tracking rows.
                foreach (var relDir in remoteDeletedDirs)
                {
                    var dirPrefix = relDir + "/";
                    var staleFolderKeys = trackedFolders.Keys
                        .Where(k => string.Equals(k, relDir, StringComparison.Ordinal) || k.StartsWith(dirPrefix, StringComparison.Ordinal))
                        .ToList();
                    foreach (var key in staleFolderKeys)
                    {
                        if (trackedFolders.Remove(key, out var staleDir))
                        {
                            await _repository.DeleteTrackedFileAsync(staleDir.Id, cancellationToken);
                        }
                    }
                }
            }
        }

        // 2. Whole-folder deletion: folder deleted locally on PC -> trash entire remote folder once.
        var localDeletedDirs = FilterTopMostFolders(previouslySyncedDirs.Where(relDir =>
            remoteFoldersByRelPath.ContainsKey(relDir) &&
            !localDirSet.Contains(relDir) &&
            !Directory.Exists(ResolveSafeLocalPath(folder.LocalPath, relDir))));

        foreach (var relDir in localDeletedDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!remoteFoldersByRelPath.TryGetValue(relDir, out var remoteDir))
            {
                continue;
            }

            var dirPrefix = relDir + "/";
            var localDirPath = ResolveSafeLocalPath(folder.LocalPath, relDir);
            var totalBytes = remoteFilesByRelPath
                .Where(kv => kv.Key.StartsWith(dirPrefix, StringComparison.Ordinal))
                .Sum(kv => kv.Value.SizeBytes);

            var deleteQueueItem = CreateTransferItem(
                folder.Id,
                remoteDir.Name,
                relDir,
                localDirPath,
                remoteDir.Id,
                TransferDirection.Delete,
                totalBytes);
            deleteQueueItem.Status = TransferStatus.InProgress;
            PublishTransfer(deleteQueueItem);

            try
            {
                await _driveClient.TrashFileAsync(remoteDir.Id, cancellationToken);

                deleteQueueItem.TransferredBytes = totalBytes;
                deleteQueueItem.Status = TransferStatus.Completed;
                deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                PublishTransfer(deleteQueueItem);
            }
            catch (OperationCanceledException)
            {
                deleteQueueItem.Status = TransferStatus.Cancelled;
                deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                PublishTransfer(deleteQueueItem);
                throw;
            }
            catch (Exception ex)
            {
                deleteQueueItem.Status = TransferStatus.Failed;
                deleteQueueItem.ErrorMessage = ex.Message;
                deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                PublishTransfer(deleteQueueItem);
                throw;
            }

            trashedAny = true;
            changesCount++;
            await RemoveTrackedSubtreeAsync(relDir, trackedFolders, trackedFiles, cancellationToken);
            RemoveSubtreeFromMap(remoteFoldersByRelPath, relDir);
            RemoveSubtreeFromMap(remoteFilesByRelPath, relDir);

            await LogAsync(ActivitySeverity.Warning, "RemoteTrashed",
                $"Exclusão local segura: pasta enviada para Lixeira do Google Drive ({relDir}).",
                folder.Id, relDir, cancellationToken);
        }

        var concurrencyLimit = _settingsService.Current.GetEffectiveTransferConcurrencyLimit();
        using var transferGate = concurrencyLimit is int limit ? new SemaphoreSlim(limit, limit) : null;
        using var remoteFolderGate = new SemaphoreSlim(1, 1);
        var transferErrors = new ConcurrentQueue<Exception>();

        async Task RunTransferAsync(TransferQueueItem? preQueuedItem, Func<Task> action)
        {
            if (transferGate is not null)
            {
                try
                {
                    await transferGate.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    if (preQueuedItem is not null)
                    {
                        preQueuedItem.Status = TransferStatus.Cancelled;
                        preQueuedItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                        PublishTransfer(preQueuedItem);
                    }
                    throw;
                }
            }

            try
            {
                await action();
                Interlocked.Increment(ref changesCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                transferErrors.Enqueue(ex);
            }
            finally
            {
                transferGate?.Release();
            }
        }

        var localTransferTasks = new List<Task>();

        foreach (var (relPath, localFile) in localFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            trackedFiles.TryGetValue(relPath, out var tracked);
            remoteFilesByRelPath.TryGetValue(relPath, out var remoteFile);

            var hashes = await FileHashUtility.ComputeHashesAsync(localFile.FullName, cancellationToken);
            var localModUtc = new DateTimeOffset(localFile.LastWriteTimeUtc, TimeSpan.Zero);

            if (remoteFile is not null)
            {
                if (string.Equals(hashes.Md5Hex, remoteFile.Md5Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    if (tracked is null ||
                        !string.Equals(tracked.RemoteFileId, remoteFile.Id, StringComparison.Ordinal) ||
                        !string.Equals(tracked.Md5Hash, hashes.Md5Hex, StringComparison.OrdinalIgnoreCase) ||
                        tracked.SizeBytes != localFile.Length)
                    {
                        await UpsertTrackedAsync(folder.Id, relPath, remoteFile, hashes, localFile.Length, localModUtc, cancellationToken);
                    }
                    continue;
                }

                if (folder.Mode == SyncMode.OneWayBackup)
                {
                    var capturedRelPath = relPath;
                    var capturedLocalFile = localFile;
                    var capturedRemoteFile = remoteFile;
                    var capturedHashes = hashes;
                    var queued = CreateTransferItem(
                        folder.Id,
                        capturedLocalFile.Name,
                        capturedRelPath,
                        capturedLocalFile.FullName,
                        capturedRemoteFile.Id,
                        TransferDirection.Upload,
                        capturedLocalFile.Length);
                    localTransferTasks.Add(RunTransferAsync(queued, () =>
                        UploadAndTrackAsync(folder, capturedRelPath, capturedLocalFile, capturedRemoteFile.ParentIds.FirstOrDefault() ?? folder.RemoteFolderId, capturedRemoteFile.Id, capturedHashes, cancellationToken, queued)));
                    continue;
                }

                var localChangedSinceTrack = tracked is null ||
                    !string.Equals(tracked.Md5Hash, hashes.Md5Hex, StringComparison.OrdinalIgnoreCase);
                var remoteChangedSinceTrack = tracked is null ||
                    (!string.IsNullOrWhiteSpace(remoteFile.Md5Checksum)
                        ? !string.Equals(tracked.Md5Hash, remoteFile.Md5Checksum, StringComparison.OrdinalIgnoreCase)
                        : remoteFile.SizeBytes != tracked.SizeBytes ||
                          Math.Abs((remoteFile.ModifiedTimeUtc - tracked.RemoteModifiedAtUtc).TotalSeconds) >= 2);

                if (localChangedSinceTrack && !remoteChangedSinceTrack)
                {
                    var capturedRelPath = relPath;
                    var capturedLocalFile = localFile;
                    var capturedRemoteFile = remoteFile;
                    var capturedHashes = hashes;
                    var queued = CreateTransferItem(
                        folder.Id,
                        capturedLocalFile.Name,
                        capturedRelPath,
                        capturedLocalFile.FullName,
                        capturedRemoteFile.Id,
                        TransferDirection.Upload,
                        capturedLocalFile.Length);
                    localTransferTasks.Add(RunTransferAsync(queued, () =>
                        UploadAndTrackAsync(folder, capturedRelPath, capturedLocalFile, capturedRemoteFile.ParentIds.FirstOrDefault() ?? folder.RemoteFolderId, capturedRemoteFile.Id, capturedHashes, cancellationToken, queued)));
                }
                else if (!localChangedSinceTrack && remoteChangedSinceTrack)
                {
                    var capturedRelPath = relPath;
                    var capturedLocalFile = localFile;
                    var capturedRemoteFile = remoteFile;
                    var queued = CreateTransferItem(
                        folder.Id,
                        capturedRemoteFile.Name,
                        capturedRelPath,
                        capturedLocalFile.FullName,
                        capturedRemoteFile.Id,
                        TransferDirection.Download,
                        capturedRemoteFile.SizeBytes);
                    localTransferTasks.Add(RunTransferAsync(queued, () =>
                        DownloadAndTrackAsync(folder, capturedRelPath, capturedLocalFile.FullName, capturedRemoteFile, cancellationToken, queued)));
                }
                else
                {
                    var capturedRelPath = relPath;
                    var capturedLocalFile = localFile;
                    var capturedRemoteFile = remoteFile;
                    var capturedHashes = hashes;
                    var capturedModUtc = localModUtc;
                    localTransferTasks.Add(RunTransferAsync(null, () =>
                        ResolveConflictAsync(folder, capturedRelPath, capturedLocalFile, capturedHashes, capturedModUtc, capturedRemoteFile, cancellationToken)));
                }
            }
            else if (tracked is not null)
            {
                if (folder.Mode == SyncMode.TwoWay)
                {
                    var deleteQueueItem = CreateTransferItem(
                        folder.Id,
                        localFile.Name,
                        relPath,
                        localFile.FullName,
                        tracked.RemoteFileId,
                        TransferDirection.Delete,
                        localFile.Length);
                    deleteQueueItem.Status = TransferStatus.InProgress;
                    PublishTransfer(deleteQueueItem);

                    try
                    {
                        SuppressWatcherForFolder(folder.Id);
                        try
                        {
                            MoveLocalFileToSystemTrash(localFile.FullName);
                        }
                        finally
                        {
                            ReleaseWatcherSuppressionDelayed(folder.Id);
                        }

                        deleteQueueItem.TransferredBytes = localFile.Length;
                        deleteQueueItem.Status = TransferStatus.Completed;
                        deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                        PublishTransfer(deleteQueueItem);
                    }
                    catch (OperationCanceledException)
                    {
                        deleteQueueItem.Status = TransferStatus.Cancelled;
                        deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                        PublishTransfer(deleteQueueItem);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        deleteQueueItem.Status = TransferStatus.Failed;
                        deleteQueueItem.ErrorMessage = ex.Message;
                        deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                        PublishTransfer(deleteQueueItem);
                        throw;
                    }

                    await _repository.DeleteTrackedFileAsync(tracked.Id, cancellationToken);
                    changesCount++;
                    await LogAsync(ActivitySeverity.Info, "LocalTrashed",
                        $"Arquivo movido para lixeira local após exclusão remota: {relPath}.",
                        folder.Id, relPath, cancellationToken);
                }
                else
                {
                    // In OneWayBackup, keep the local file without re-uploading after Drive deletion
                    // unless the local file is edited again.
                    var localModifiedSinceBackup = !string.Equals(tracked.Md5Hash, hashes.Md5Hex, StringComparison.OrdinalIgnoreCase);
                    if (localModifiedSinceBackup)
                    {
                        var capturedRelPath = relPath;
                        var capturedLocalFile = localFile;
                        var capturedHashes = hashes;
                        var queued = CreateTransferItem(
                            folder.Id,
                            capturedLocalFile.Name,
                            capturedRelPath,
                            capturedLocalFile.FullName,
                            null,
                            TransferDirection.Upload,
                            capturedLocalFile.Length);
                        localTransferTasks.Add(RunTransferAsync(queued, async () =>
                        {
                            var parentId = await EnsureRemoteParentFolderAsync(folder.RemoteFolderId, capturedRelPath, remoteFoldersByRelPath, remoteFolderGate, cancellationToken);
                            await UploadAndTrackAsync(folder, capturedRelPath, capturedLocalFile, parentId, null, capturedHashes, cancellationToken, queued);
                        }));
                    }
                    else if (!string.IsNullOrEmpty(tracked.RemoteFileId))
                    {
                        tracked.RemoteFileId = string.Empty;
                        tracked.RemoteParentId = string.Empty;
                        await _repository.UpsertTrackedFileAsync(tracked, cancellationToken);
                    }
                }
            }
            else
            {
                var capturedRelPath = relPath;
                var capturedLocalFile = localFile;
                var capturedHashes = hashes;
                var queued = CreateTransferItem(
                    folder.Id,
                    capturedLocalFile.Name,
                    capturedRelPath,
                    capturedLocalFile.FullName,
                    null,
                    TransferDirection.Upload,
                    capturedLocalFile.Length);
                localTransferTasks.Add(RunTransferAsync(queued, async () =>
                {
                    var parentId = await EnsureRemoteParentFolderAsync(folder.RemoteFolderId, capturedRelPath, remoteFoldersByRelPath, remoteFolderGate, cancellationToken);
                    await UploadAndTrackAsync(folder, capturedRelPath, capturedLocalFile, parentId, null, capturedHashes, cancellationToken, queued);
                }));
            }
        }

        if (localTransferTasks.Count > 0)
        {
            await Task.WhenAll(localTransferTasks);
        }

        foreach (var (relPath, tracked) in trackedFiles)
        {
            if (localFiles.ContainsKey(relPath))
            {
                continue;
            }

            if (remoteFilesByRelPath.TryGetValue(relPath, out var remoteFile))
            {
                var deleteQueueItem = CreateTransferItem(
                    folder.Id,
                    remoteFile.Name,
                    relPath,
                    Path.Combine(folder.LocalPath, relPath.Replace('/', Path.DirectorySeparatorChar)),
                    remoteFile.Id,
                    TransferDirection.Delete,
                    remoteFile.SizeBytes);
                deleteQueueItem.Status = TransferStatus.InProgress;
                PublishTransfer(deleteQueueItem);

                try
                {
                    // Safe deletion: move to Drive Trash (Trashed = true)
                    await _driveClient.TrashFileAsync(remoteFile.Id, cancellationToken);

                    deleteQueueItem.TransferredBytes = remoteFile.SizeBytes;
                    deleteQueueItem.Status = TransferStatus.Completed;
                    deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                    PublishTransfer(deleteQueueItem);
                }
                catch (OperationCanceledException)
                {
                    deleteQueueItem.Status = TransferStatus.Cancelled;
                    deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                    PublishTransfer(deleteQueueItem);
                    throw;
                }
                catch (Exception ex)
                {
                    deleteQueueItem.Status = TransferStatus.Failed;
                    deleteQueueItem.ErrorMessage = ex.Message;
                    deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                    PublishTransfer(deleteQueueItem);
                    throw;
                }

                trashedAny = true;
                changesCount++;
                await LogAsync(ActivitySeverity.Warning, "RemoteTrashed",
                    $"Exclusão local segura: arquivo enviado para Lixeira do Google Drive ({relPath}).",
                    folder.Id, relPath, cancellationToken);
            }

            await _repository.DeleteTrackedFileAsync(tracked.Id, cancellationToken);
        }

        if (trashedAny)
        {
            await Task.Delay(750, cancellationToken);
            await RefreshAccountInfoAsync(cancellationToken);
        }

        if (folder.Mode == SyncMode.TwoWay)
        {
            var remoteDownloadTasks = new List<Task>();
            foreach (var (relPath, remoteFile) in remoteFilesByRelPath)
            {
                if (localFiles.ContainsKey(relPath) || trackedFiles.ContainsKey(relPath))
                {
                    continue;
                }

                if (SyncIgnoreMatcher.IsIgnored(relPath, ignorePatterns))
                {
                    continue;
                }

                var capturedRelPath = relPath;
                var capturedRemoteFile = remoteFile;
                var targetLocalPath = ResolveSafeLocalPath(folder.LocalPath, capturedRelPath);
                var queued = CreateTransferItem(
                    folder.Id,
                    capturedRemoteFile.Name,
                    capturedRelPath,
                    targetLocalPath,
                    capturedRemoteFile.Id,
                    TransferDirection.Download,
                    capturedRemoteFile.SizeBytes);
                remoteDownloadTasks.Add(RunTransferAsync(queued, () =>
                    DownloadAndTrackAsync(folder, capturedRelPath, targetLocalPath, capturedRemoteFile, cancellationToken, queued)));
            }

            if (remoteDownloadTasks.Count > 0)
            {
                await Task.WhenAll(remoteDownloadTasks);
            }

            // Ensure empty remote folders also exist locally in TwoWay mode.
            foreach (var relDir in remoteFoldersByRelPath.Keys.OrderBy(k => k.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SyncIgnoreMatcher.IsIgnored(relDir + "/", ignorePatterns) || trackedFolders.ContainsKey(relDir))
                {
                    continue;
                }

                var targetDir = ResolveSafeLocalPath(folder.LocalPath, relDir);
                if (!Directory.Exists(targetDir))
                {
                    SuppressWatcherForFolder(folder.Id);
                    try
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    finally
                    {
                        ReleaseWatcherSuppressionDelayed(folder.Id);
                    }

                    localDirs.Add(relDir);
                    localDirSet.Add(relDir);
                    changesCount++;
                }
            }
        }

        // Ensure empty local subdirectories also exist in Drive.
        foreach (var relDir in localDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!remoteFoldersByRelPath.ContainsKey(relDir))
            {
                await EnsureRemoteParentFolderAsync(folder.RemoteFolderId, $"{relDir}/.dir-placeholder", remoteFoldersByRelPath, remoteFolderGate, cancellationToken);
                Interlocked.Increment(ref changesCount);
            }
        }

        // Persist tracked folder rows for synced directories so whole-folder deletion works both ways.
        foreach (var (relDir, remoteDir) in remoteFoldersByRelPath)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localDirPath = ResolveSafeLocalPath(folder.LocalPath, relDir);
            if (!Directory.Exists(localDirPath))
            {
                continue;
            }

            if (!trackedFolders.TryGetValue(relDir, out var existingDir) ||
                !string.Equals(existingDir.RemoteFileId, remoteDir.Id, StringComparison.Ordinal))
            {
                await UpsertTrackedFolderAsync(folder.Id, relDir, remoteDir, localDirPath, existingDir, cancellationToken);
            }
        }

        if (!transferErrors.IsEmpty)
        {
            if (transferErrors.Count == 1 && transferErrors.TryDequeue(out var singleErr))
            {
                throw singleErr;
            }

            throw new AggregateException($"{transferErrors.Count} transferência(s) falharam durante a sincronização.", transferErrors.ToArray());
        }

        return changesCount;
    }

    private async Task ResolveConflictAsync(
        LocalSyncFolder folder,
        string relPath,
        FileInfo localFile,
        FileHashResult localHashes,
        DateTimeOffset localModUtc,
        RemoteDriveNode remoteFile,
        CancellationToken cancellationToken)
    {
        var policy = _settingsService.Current.ConflictPolicy;
        var preferLocal = policy switch
        {
            ConflictResolutionPolicy.PreferLocal => true,
            ConflictResolutionPolicy.PreferRemote => false,
            ConflictResolutionPolicy.KeepNewest => localModUtc >= remoteFile.ModifiedTimeUtc,
            _ => false
        };

        if (policy == ConflictResolutionPolicy.RenameConflictCopy)
        {
            var conflictCopyPath = BuildConflictCopyPath(localFile.FullName);
            File.Copy(localFile.FullName, conflictCopyPath, overwrite: false);
            await DownloadAndTrackAsync(folder, relPath, localFile.FullName, remoteFile, cancellationToken);
            await LogAsync(ActivitySeverity.Warning, "ConflictRenamed",
                $"Conflito detectado em {relPath}; cópia local preservada como {Path.GetFileName(conflictCopyPath)}.",
                folder.Id, relPath, cancellationToken);
            return;
        }

        if (preferLocal)
        {
            await UploadAndTrackAsync(
                folder,
                relPath,
                localFile,
                remoteFile.ParentIds.FirstOrDefault() ?? folder.RemoteFolderId,
                remoteFile.Id,
                localHashes,
                cancellationToken);
        }
        else
        {
            await DownloadAndTrackAsync(folder, relPath, localFile.FullName, remoteFile, cancellationToken);
        }
    }

    private async Task UploadAndTrackAsync(
        LocalSyncFolder folder,
        string relPath,
        FileInfo localFile,
        string remoteParentId,
        string? existingRemoteFileId,
        FileHashResult hashes,
        CancellationToken cancellationToken,
        TransferQueueItem? existingQueueItem = null)
    {
        var queueItem = existingQueueItem ?? CreateTransferItem(
            folder.Id,
            localFile.Name,
            relPath,
            localFile.FullName,
            existingRemoteFileId,
            TransferDirection.Upload,
            localFile.Length);
        queueItem.RemoteParentId = remoteParentId;
        queueItem.TotalBytes = localFile.Length;
        queueItem.ErrorMessage = null;
        queueItem.CompletedAtUtc = null;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _transferCancellations[queueItem.Id] = linkedCts;

        try
        {
            queueItem.Status = TransferStatus.InProgress;
            PublishTransfer(queueItem);

            var uploaded = await UploadFileWithResumeAsync(
                syncFolderId: folder.Id,
                fileInfo: localFile,
                parentFolderId: remoteParentId,
                existingRemoteFileId: existingRemoteFileId,
                queueItem: queueItem,
                cancellationToken: linkedCts.Token);

            queueItem.RemoteFileId = uploaded.Id;
            queueItem.TransferredBytes = queueItem.TotalBytes;
            queueItem.Status = TransferStatus.Completed;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);

            var localModUtc = new DateTimeOffset(localFile.LastWriteTimeUtc, TimeSpan.Zero);
            await UpsertTrackedAsync(folder.Id, relPath, uploaded, hashes, localFile.Length, localModUtc, cancellationToken);

            await LogAsync(ActivitySeverity.Success, "Uploaded",
                $"Enviado para o Drive ({folder.RemoteFolderName}): {localFile.FullName}.",
                folder.Id, localFile.FullName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            queueItem.Status = TransferStatus.Cancelled;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            throw;
        }
        catch (Exception ex)
        {
            queueItem.Status = TransferStatus.Failed;
            queueItem.ErrorMessage = ex.Message;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            await LogAsync(ActivitySeverity.Error, "UploadFailed",
                $"Falha ao enviar {localFile.Name}: {ex.Message}",
                folder.Id, localFile.FullName, CancellationToken.None);
            throw;
        }
        finally
        {
            _transferCancellations.TryRemove(queueItem.Id, out _);
        }
    }

    private async Task DownloadAndTrackAsync(
        LocalSyncFolder folder,
        string relPath,
        string targetLocalPath,
        RemoteDriveNode remoteFile,
        CancellationToken cancellationToken,
        TransferQueueItem? existingQueueItem = null)
    {
        var queueItem = existingQueueItem ?? CreateTransferItem(
            folder.Id,
            remoteFile.Name,
            relPath,
            targetLocalPath,
            remoteFile.Id,
            TransferDirection.Download,
            remoteFile.SizeBytes);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _transferCancellations[queueItem.Id] = linkedCts;

        SuppressWatcherForFolder(folder.Id);
        try
        {
            queueItem.TotalBytes = remoteFile.SizeBytes;
            queueItem.ErrorMessage = null;
            queueItem.CompletedAtUtc = null;
            queueItem.Status = TransferStatus.InProgress;
            PublishTransfer(queueItem);

            await DownloadFileWithResumeAsync(
                remoteFileId: remoteFile.Id,
                targetLocalPath: targetLocalPath,
                knownTotalBytes: remoteFile.SizeBytes,
                queueItem: queueItem,
                cancellationToken: linkedCts.Token);

            if (File.Exists(targetLocalPath))
            {
                File.SetLastWriteTimeUtc(targetLocalPath, remoteFile.ModifiedTimeUtc.UtcDateTime);
            }

            var hashes = await FileHashUtility.ComputeHashesAsync(targetLocalPath, cancellationToken);
            var localInfo = new FileInfo(targetLocalPath);

            queueItem.TransferredBytes = queueItem.TotalBytes;
            queueItem.Status = TransferStatus.Completed;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);

            await UpsertTrackedAsync(
                folder.Id,
                relPath,
                remoteFile,
                hashes,
                localInfo.Length,
                new DateTimeOffset(localInfo.LastWriteTimeUtc, TimeSpan.Zero),
                cancellationToken);

            await LogAsync(ActivitySeverity.Success, "Downloaded",
                $"Baixado do Drive para {targetLocalPath}.",
                folder.Id, targetLocalPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            queueItem.Status = TransferStatus.Cancelled;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            throw;
        }
        catch (Exception ex)
        {
            queueItem.Status = TransferStatus.Failed;
            queueItem.ErrorMessage = ex.Message;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);
            await LogAsync(ActivitySeverity.Error, "DownloadFailed",
                $"Falha ao baixar {remoteFile.Name}: {ex.Message}",
                folder.Id, targetLocalPath, CancellationToken.None);
            throw;
        }
        finally
        {
            _transferCancellations.TryRemove(queueItem.Id, out _);
            ReleaseWatcherSuppressionDelayed(folder.Id);
        }
    }

    private async Task UpsertTrackedAsync(
        string syncFolderId,
        string relPath,
        RemoteDriveNode remoteFile,
        FileHashResult hashes,
        long sizeBytes,
        DateTimeOffset localModUtc,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.GetTrackedFileAsync(syncFolderId, relPath, cancellationToken);
        var entry = existing ?? new TrackedFileEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            SyncFolderId = syncFolderId,
            RelativePath = relPath
        };

        entry.RemoteFileId = remoteFile.Id;
        entry.RemoteParentId = remoteFile.ParentIds.FirstOrDefault() ?? string.Empty;
        entry.IsFolder = false;
        entry.Md5Hash = hashes.Md5Hex;
        entry.Sha256Hash = hashes.Sha256Hex;
        entry.SizeBytes = sizeBytes;
        entry.LocalModifiedAtUtc = localModUtc;
        entry.RemoteModifiedAtUtc = remoteFile.ModifiedTimeUtc;
        entry.LastSyncedAtUtc = DateTimeOffset.UtcNow;

        await _repository.UpsertTrackedFileAsync(entry, cancellationToken);
    }

    private async Task UpsertTrackedFolderAsync(
        string syncFolderId,
        string relDir,
        RemoteDriveNode remoteDir,
        string localDirPath,
        TrackedFileEntry? existing,
        CancellationToken cancellationToken)
    {
        var dirInfo = new DirectoryInfo(localDirPath);
        var entry = existing ?? await _repository.GetTrackedFileAsync(syncFolderId, relDir, cancellationToken) ?? new TrackedFileEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            SyncFolderId = syncFolderId,
            RelativePath = relDir
        };

        entry.RemoteFileId = remoteDir.Id;
        entry.RemoteParentId = remoteDir.ParentIds.FirstOrDefault() ?? string.Empty;
        entry.IsFolder = true;
        entry.Md5Hash = null;
        entry.Sha256Hash = null;
        entry.SizeBytes = 0;
        entry.LocalModifiedAtUtc = dirInfo.Exists
            ? new DateTimeOffset(dirInfo.LastWriteTimeUtc, TimeSpan.Zero)
            : DateTimeOffset.UtcNow;
        entry.RemoteModifiedAtUtc = remoteDir.ModifiedTimeUtc;
        entry.LastSyncedAtUtc = DateTimeOffset.UtcNow;

        await _repository.UpsertTrackedFileAsync(entry, cancellationToken);
    }

    private async Task RemoveTrackedSubtreeAsync(
        string relDir,
        Dictionary<string, TrackedFileEntry> trackedFolders,
        Dictionary<string, TrackedFileEntry> trackedFiles,
        CancellationToken cancellationToken)
    {
        var prefix = relDir + "/";

        var folderKeys = trackedFolders.Keys
            .Where(k => string.Equals(k, relDir, StringComparison.Ordinal) || k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        foreach (var key in folderKeys)
        {
            if (trackedFolders.Remove(key, out var entry))
            {
                await _repository.DeleteTrackedFileAsync(entry.Id, cancellationToken);
            }
        }

        var fileKeys = trackedFiles.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        foreach (var key in fileKeys)
        {
            if (trackedFiles.Remove(key, out var entry))
            {
                await _repository.DeleteTrackedFileAsync(entry.Id, cancellationToken);
            }
        }
    }

    private static void RemoveSubtreeFromMap<T>(Dictionary<string, T> map, string relDir)
    {
        var prefix = relDir + "/";
        var keys = map.Keys
            .Where(k => string.Equals(k, relDir, StringComparison.Ordinal) || k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        foreach (var key in keys)
        {
            map.Remove(key);
        }
    }

    private static IEnumerable<string> EnumerateAncestorDirectories(string relativeFilePath)
    {
        var normalized = relativeFilePath.Replace('\\', '/').Trim('/');
        var idx = normalized.IndexOf('/');
        while (idx > 0)
        {
            yield return normalized[..idx];
            idx = normalized.IndexOf('/', idx + 1);
        }
    }

    private static List<string> FilterTopMostFolders(IEnumerable<string> folderPaths)
    {
        var ordered = folderPaths
            .Select(p => p.Replace('\\', '/').Trim('/'))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p.Length)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();

        var topMost = new List<string>();
        foreach (var candidate in ordered)
        {
            var hasSelectedAncestor = topMost.Any(parent =>
                candidate.StartsWith(parent + "/", StringComparison.Ordinal));
            if (!hasSelectedAncestor)
            {
                topMost.Add(candidate);
            }
        }

        return topMost;
    }

    private async Task<string> EnsureRemoteParentFolderAsync(
        string rootRemoteFolderId,
        string relativeFilePath,
        Dictionary<string, RemoteDriveNode> remoteFoldersByRelPath,
        SemaphoreSlim folderGate,
        CancellationToken cancellationToken)
    {
        var dirPart = Path.GetDirectoryName(relativeFilePath)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(dirPart) || dirPart == ".")
        {
            return rootRemoteFolderId;
        }

        await folderGate.WaitAsync(cancellationToken);
        try
        {
            if (remoteFoldersByRelPath.TryGetValue(dirPart, out var existing))
            {
                return existing.Id;
            }

            var segments = dirPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentParentId = rootRemoteFolderId;
            var builtPath = string.Empty;

            foreach (var segment in segments)
            {
                builtPath = string.IsNullOrEmpty(builtPath) ? segment : $"{builtPath}/{segment}";
                if (remoteFoldersByRelPath.TryGetValue(builtPath, out var found))
                {
                    currentParentId = found.Id;
                    continue;
                }

                var created = await _driveClient.CreateFolderAsync(segment, currentParentId, cancellationToken);
                remoteFoldersByRelPath[builtPath] = created;
                currentParentId = created.Id;
            }

            return currentParentId;
        }
        finally
        {
            folderGate.Release();
        }
    }

    private async Task<RemoteTreeSnapshot> ScanRemoteTreeAsync(
        string rootFolderId,
        CancellationToken cancellationToken)
    {
        var foldersByPath = new ConcurrentDictionary<string, RemoteDriveNode>(StringComparer.Ordinal);
        var filesByPath = new ConcurrentDictionary<string, RemoteDriveNode>(StringComparer.Ordinal);
        var visitedFolderIds = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        using var concurrencyGate = new SemaphoreSlim(6, 6);

        async Task ScanFolderRecursiveAsync(string folderId, string prefix)
        {
            if (!visitedFolderIds.TryAdd(folderId, 0))
            {
                return;
            }

            IReadOnlyList<RemoteDriveNode> children;
            await concurrencyGate.WaitAsync(cancellationToken);
            try
            {
                children = await _driveClient.ListChildrenAsync(folderId, foldersOnly: false, cancellationToken);
            }
            finally
            {
                concurrencyGate.Release();
            }

            var subTasks = new List<Task>();
            foreach (var child in children)
            {
                if (child.Trashed ||
                    string.Equals(child.MimeType, "application/vnd.google-apps.shortcut", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var safeName = SanitizeRemoteSegment(child.Name);
                var relPath = string.IsNullOrEmpty(prefix) ? safeName : $"{prefix}/{safeName}";

                if (child.IsFolder)
                {
                    foldersByPath[relPath] = child;
                    subTasks.Add(ScanFolderRecursiveAsync(child.Id, relPath));
                }
                else if (child.IsGoogleWorkspaceDoc)
                {
                    _logger.LogDebug("Skipping native Google Workspace file '{FileName}' during binary sync.", relPath);
                }
                else
                {
                    filesByPath[relPath] = child;
                }
            }

            if (subTasks.Count > 0)
            {
                await Task.WhenAll(subTasks);
            }
        }

        await ScanFolderRecursiveAsync(rootFolderId, string.Empty);

        return new RemoteTreeSnapshot(
            new Dictionary<string, RemoteDriveNode>(foldersByPath, StringComparer.Ordinal),
            new Dictionary<string, RemoteDriveNode>(filesByPath, StringComparer.Ordinal));
    }

    private static Dictionary<string, FileInfo> ScanLocalFiles(
        string rootLocalPath,
        IReadOnlyList<string> ignorePatterns)
    {
        var result = new Dictionary<string, FileInfo>(StringComparer.Ordinal);
        var rootInfo = new DirectoryInfo(rootLocalPath);
        if (!rootInfo.Exists)
        {
            return result;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var file in rootInfo.EnumerateFiles("*", options))
        {
            var relPath = Path.GetRelativePath(rootLocalPath, file.FullName).Replace('\\', '/');
            if (SyncIgnoreMatcher.IsIgnored(relPath, ignorePatterns))
            {
                continue;
            }

            result[relPath] = file;
        }

        return result;
    }

    private static List<string> ScanLocalDirectories(
        string rootLocalPath,
        IReadOnlyList<string> ignorePatterns)
    {
        var result = new List<string>();
        var rootInfo = new DirectoryInfo(rootLocalPath);
        if (!rootInfo.Exists)
        {
            return result;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var dir in rootInfo.EnumerateDirectories("*", options))
        {
            var relPath = Path.GetRelativePath(rootLocalPath, dir.FullName).Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(relPath) || SyncIgnoreMatcher.IsIgnored(relPath + "/", ignorePatterns))
            {
                continue;
            }

            result.Add(relPath);
        }

        return result;
    }

    private static string BuildConflictCopyPath(string originalPath)
    {
        var dir = Path.GetDirectoryName(originalPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(originalPath);
        var ext = Path.GetExtension(originalPath);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(dir, $"{name}.conflito-{stamp}{ext}");
    }

    internal string? TrashDirectoryOverride { get; set; }

    private void MoveLocalFileToSystemTrash(string localFilePath) =>
        MoveLocalPathToSystemTrash(localFilePath, isDirectory: false);

    private void MoveLocalDirectoryToSystemTrash(string localDirPath) =>
        MoveLocalPathToSystemTrash(localDirPath, isDirectory: true);

    private void MoveLocalPathToSystemTrash(string localPath, bool isDirectory)
    {
        var normalizedPath = isDirectory
            ? Path.TrimEndingDirectorySeparator(localPath)
            : localPath;

        if (isDirectory ? !Directory.Exists(normalizedPath) : !File.Exists(normalizedPath))
        {
            return;
        }

        string? infoFile = null;
        try
        {
            var trashBase = !string.IsNullOrWhiteSpace(TrashDirectoryOverride)
                ? TrashDirectoryOverride
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Trash");
            var trashFiles = Path.Combine(trashBase, "files");
            var trashInfo = Path.Combine(trashBase, "info");
            Directory.CreateDirectory(trashFiles);
            Directory.CreateDirectory(trashInfo);

            var itemName = Path.GetFileName(normalizedPath);
            var destName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{itemName}";
            var destPath = Path.Combine(trashFiles, destName);
            infoFile = Path.Combine(trashInfo, $"{destName}.trashinfo");

            var trashMetadata = $"""
                [Trash Info]
                Path={EncodeFreeDesktopTrashPath(normalizedPath)}
                DeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}
                """;
            File.WriteAllText(infoFile, trashMetadata);

            if (isDirectory)
            {
                Directory.Move(normalizedPath, destPath);
            }
            else
            {
                File.Move(normalizedPath, destPath, overwrite: true);
            }
        }
        catch
        {
            if (!string.IsNullOrEmpty(infoFile) && File.Exists(infoFile))
            {
                try
                {
                    File.Delete(infoFile);
                }
                catch
                {
                }
            }

            if (isDirectory)
            {
                if (Directory.Exists(normalizedPath))
                {
                    Directory.Delete(normalizedPath, recursive: true);
                }
            }
            else if (File.Exists(normalizedPath))
            {
                File.Delete(normalizedPath);
            }
        }
    }

    internal static string EncodeFreeDesktopTrashPath(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath);
        var hasRoot = normalized.StartsWith('/');
        var encodedSegments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);
        var joined = string.Join('/', encodedSegments);
        return hasRoot ? "/" + joined : joined;
    }

    private TransferQueueItem CreateTransferItem(
        string syncFolderId,
        string fileName,
        string relativePath,
        string localPath,
        string? remoteFileId,
        TransferDirection direction,
        long totalBytes)
    {
        var item = new TransferQueueItem
        {
            SyncFolderId = syncFolderId,
            FileName = fileName,
            RelativePath = relativePath,
            LocalPath = localPath,
            RemoteFileId = remoteFileId,
            Direction = direction,
            Status = TransferStatus.Queued,
            TotalBytes = totalBytes,
            TransferredBytes = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        _transfers[item.Id] = item;
        PruneOldCompletedTransfers();
        PublishTransfer(item);
        return item;
    }

    private void PruneOldCompletedTransfers()
    {
        const int maxRetained = 2000;
        if (_transfers.Count <= maxRetained)
        {
            return;
        }

        var overflow = _transfers.Count - maxRetained;
        var candidates = _transfers.Values
            .Where(t => t.Status is TransferStatus.Completed or TransferStatus.Cancelled or TransferStatus.Failed)
            .OrderBy(t => t.CreatedAtUtc)
            .Take(overflow)
            .ToList();

        foreach (var item in candidates)
        {
            if (_transfers.TryRemove(item.Id, out var removed))
            {
                switch (removed.Status)
                {
                    case TransferStatus.Completed:
                        Interlocked.Increment(ref _prunedCompletedCount);
                        break;
                    case TransferStatus.Failed:
                        Interlocked.Increment(ref _prunedFailedCount);
                        break;
                    case TransferStatus.Cancelled:
                        Interlocked.Increment(ref _prunedCancelledCount);
                        break;
                }
            }
        }
    }

    private async Task TrySendLifecycleEmailAsync(
        string recipientEmail,
        string? recipientName,
        bool isConnected,
        CancellationToken cancellationToken = default)
    {
        if (_mailerClient is null)
        {
            return;
        }

        try
        {
            var language = _settingsService.Current.Language;
            var actionText = isConnected ? "boas-vindas (conectada)" : "revogação (desconectada)";
            var sent = await _mailerClient.TrySendLifecycleEmailAsync(
                recipientEmail,
                recipientName,
                isConnected,
                language,
                cancellationToken);

            if (sent)
            {
                await LogAsync(ActivitySeverity.Info, "GmailAlertSent",
                    $"E-mail oficial ({AppsScriptMailerClient.OfficialSenderEmail}) enviado para {recipientEmail} ({actionText}).",
                    null, recipientEmail, CancellationToken.None);
            }
            else
            {
                await LogAsync(ActivitySeverity.Info, "GmailAlertSkipped",
                    $"E-mail automático não enviado para {recipientEmail} (URL do Web App TundraLab não configurada ou sem rede).",
                    null, recipientEmail, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TundraLab lifecycle email did not complete for '{Email}'.", recipientEmail);
        }
    }

    private void PublishTransfer(TransferQueueItem item) => TransferUpdated?.Invoke(this, CloneTransfer(item));

    private static TransferQueueItem CloneTransfer(TransferQueueItem item) => new()
    {
        Id = item.Id,
        SyncFolderId = item.SyncFolderId,
        FileName = item.FileName,
        RelativePath = item.RelativePath,
        LocalPath = item.LocalPath,
        RemoteFileId = item.RemoteFileId,
        Direction = item.Direction,
        Status = item.Status,
        TotalBytes = item.TotalBytes,
        TransferredBytes = item.TransferredBytes,
        ErrorMessage = item.ErrorMessage,
        CreatedAtUtc = item.CreatedAtUtc,
        CompletedAtUtc = item.CompletedAtUtc
    };

    private async Task LogAsync(
        ActivitySeverity severity,
        string action,
        string message,
        string? syncFolderId,
        string? targetPath,
        CancellationToken cancellationToken)
    {
        var log = new SyncActivityLog
        {
            SyncFolderId = syncFolderId,
            Severity = severity,
            Action = action,
            Message = message,
            TargetPath = targetPath,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            log.Id = await _repository.AddActivityLogAsync(log, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist activity log.");
        }

        ActivityLogged?.Invoke(this, log);
    }

    private void SuppressWatcherForFolder(string folderId)
    {
        lock (_suppressionLock)
        {
            _suppressedWatcherCounts.TryGetValue(folderId, out var count);
            _suppressedWatcherCounts[folderId] = count + 1;
        }
    }

    private void ReleaseWatcherSuppressionDelayed(string folderId)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(1800);
            lock (_suppressionLock)
            {
                if (!_suppressedWatcherCounts.TryGetValue(folderId, out var count))
                {
                    return;
                }

                if (count <= 1)
                {
                    _suppressedWatcherCounts.Remove(folderId);
                }
                else
                {
                    _suppressedWatcherCounts[folderId] = count - 1;
                }
            }
        });
    }

    private async Task OnLocalFolderChangedAsync(string folderId)
    {
        lock (_suppressionLock)
        {
            if (_suppressedWatcherCounts.TryGetValue(folderId, out var count) && count > 0)
            {
                return;
            }
        }

        await TriggerFolderSyncAsync(folderId);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _ = Task.Run(async () =>
        {
            var folders = await GetSyncFoldersAsync();
            _watcherService.UpdateWatchers(folders);
            StartBackgroundTimer();
        });
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable)
        {
            UpdateOnlineState(false);
        }

        _ = Task.Run(() => ProbeConnectivityAsync());
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(700);
            await ProbeConnectivityAsync();
        });
    }

    public void ReportNetworkFailure()
    {
        _ = Task.Run(() => ProbeConnectivityAsync());
    }

    public async Task<bool> ProbeConnectivityAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _probeInFlight, 1) == 1)
        {
            return IsOnline;
        }

        try
        {
            if (_connectivityProbe is not null)
            {
                var customReachable = await _connectivityProbe(cancellationToken);
                UpdateOnlineState(customReachable);
                return customReachable;
            }

            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                UpdateOnlineState(false);
                return false;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var req = new HttpRequestMessage(HttpMethod.Head, "https://www.googleapis.com/generate_204");
            using var resp = await ConnectivityProbeClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var reachable = (int)resp.StatusCode is >= 200 and < 500;
            UpdateOnlineState(reachable);
            return reachable;
        }
        catch
        {
            UpdateOnlineState(false);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _probeInFlight, 0);
        }
    }

    private void UpdateOnlineState(bool nextOnline)
    {
        var prev = Interlocked.Exchange(ref _isOnlineFlag, nextOnline ? 1 : 0) == 1;
        if (prev == nextOnline)
        {
            return;
        }

        if (!nextOnline)
        {
            CancelAllActiveOperations();
        }
        else if (_driveClient.IsConnected)
        {
            _ = Task.Run(() => RefreshAccountInfoAsync());
        }

        ConnectivityChanged?.Invoke(this, nextOnline);
    }

    private void StartBackgroundTimer()
    {
        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _periodicTimer?.Dispose();

        _lifecycleCts = new CancellationTokenSource();
        var token = _lifecycleCts.Token;
        var intervalSeconds = Math.Max(30, _settingsService.Current.SyncIntervalSeconds);
        _periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        _ = Task.Run(async () =>
        {
            try
            {
                while (await _periodicTimer.WaitForNextTickAsync(token))
                {
                    if (_driveClient.IsConnected && IsOnline)
                    {
                        await TriggerSyncAllAsync(token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _watcherService.FolderChangeDetected -= OnLocalFolderChangedAsync;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _lifecycleCts?.Cancel();
        _lifecycleCts?.Dispose();
        _periodicTimer?.Dispose();

        foreach (var lck in _folderLocks.Values)
        {
            lck.Dispose();
        }
    }

    private sealed record RemoteTreeSnapshot(
        Dictionary<string, RemoteDriveNode> FoldersByRelativePath,
        Dictionary<string, RemoteDriveNode> FilesByRelativePath);
}

internal static class DriveAccountExtensions
{
    public static DriveAccount WithDisconnectedFallback(this DriveAccount account) => new()
    {
        Id = account.Id,
        DisplayName = account.DisplayName,
        EmailAddress = account.EmailAddress,
        PhotoUrl = account.PhotoUrl,
        IsConnected = false,
        ConnectedAtUtc = account.ConnectedAtUtc,
        Quota = account.Quota
    };
}
