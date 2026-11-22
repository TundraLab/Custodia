using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
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

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _folderLocks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _folderSyncCancellations = new();
    private readonly ConcurrentDictionary<string, TransferQueueItem> _transfers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _transferCancellations = new();
    private readonly object _suppressionLock = new();
    private readonly HashSet<string> _suppressedWatcherFolders = new();

    private PeriodicTimer? _periodicTimer;
    private CancellationTokenSource? _lifecycleCts;
    private int _activeSyncCount;
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
        ILogger<DriveSyncService> logger)
    {
        _driveClient = driveClient;
        _repository = repository;
        _settingsService = settingsService;
        _authManager = authManager;
        _watcherService = watcherService;
        _logger = logger;

        _isOnlineFlag = NetworkInterface.GetIsNetworkAvailable() ? 1 : 0;
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
            CurrentAccount = restored;
            await _repository.UpsertAccountAsync(restored, cancellationToken);
            await PersistActiveAccountIdAsync(restored.Id, cancellationToken);
            SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);

            var folders = await _repository.GetSyncFoldersByAccountAsync(restored.Id, cancellationToken);
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
            await _authManager.ImportCredentialsFileAsync(customCredentialsJsonPath!, candidateKey, cancellationToken);
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
                await _authManager.CopyCredentialsAsync(candidateKey, duplicate.Id, cancellationToken);
                _authManager.DeleteAccountCredentialsFile(candidateKey);
            }
            await _authManager.CopyTokenAsync(candidateKey, duplicate.Id, cancellationToken);
            await _authManager.DeleteLocalTokenAsync(candidateKey, cancellationToken);
            account.Id = duplicate.Id;
            await _driveClient.SwitchSessionAsync(duplicate.Id, cancellationToken);
        }

        CancelAllActiveOperations();

        CurrentAccount = account;
        await _repository.UpsertAccountAsync(account, cancellationToken);
        await PersistActiveAccountIdAsync(account.Id, cancellationToken);
        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);

        var folders = await _repository.GetSyncFoldersByAccountAsync(account.Id, cancellationToken);
        _watcherService.UpdateWatchers(folders);

        AccountsListChanged?.Invoke(this, EventArgs.Empty);
        AccountChanged?.Invoke(this, account);
        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

        await LogAsync(ActivitySeverity.Success, "AccountConnect",
            $"Conta Google conectada: {account.EmailAddress}.", null, null, cancellationToken);

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
        await _repository.UpsertAccountAsync(restored, cancellationToken);
        await PersistActiveAccountIdAsync(restored.Id, cancellationToken);
        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);

        var folders = await _repository.GetSyncFoldersByAccountAsync(restored.Id, cancellationToken);
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
        CancellationToken cancellationToken = default)
    {
        var targetId = string.IsNullOrWhiteSpace(accountId) ? CurrentAccount.Id : accountId;
        var disconnectedEmail = CurrentAccount.EmailAddress;

        CancelAllActiveOperations();
        _watcherService.UpdateWatchers([]);

        await _driveClient.DisconnectAsync(targetId, cancellationToken);
        await _repository.DeleteAccountAsync(targetId, cancellationToken);

        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);

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

        SavedAccounts = await _repository.GetAllAccountsAsync(cancellationToken);
        AccountsListChanged?.Invoke(this, EventArgs.Empty);
        AccountChanged?.Invoke(this, CurrentAccount);
        SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

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
            LocalPath = fullLocalPath,
            RemoteFolderId = string.IsNullOrWhiteSpace(remoteFolderId) ? "root" : remoteFolderId,
            RemoteFolderName = string.IsNullOrWhiteSpace(remoteFolderName) ? "Meu Drive" : remoteFolderName,
            Mode = mode,
            State = FolderSyncState.Idle,
            IsPaused = false
        };

        await _repository.UpsertSyncFolderAsync(folder, cancellationToken);
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

    public async Task UpdateSyncModeAsync(
        string folderId,
        SyncMode mode,
        CancellationToken cancellationToken = default)
    {
        var folder = await _repository.GetSyncFolderByIdAsync(folderId, cancellationToken);
        if (folder is null)
        {
            return;
        }

        folder.Mode = mode;
        await _repository.UpsertSyncFolderAsync(folder, cancellationToken);
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
        var folder = await _repository.GetSyncFolderByIdAsync(folderId, cancellationToken);
        if (folder is null)
        {
            return;
        }

        folder.IsPaused = isPaused;
        folder.State = isPaused ? FolderSyncState.Paused : FolderSyncState.Idle;
        await _repository.UpsertSyncFolderAsync(folder, cancellationToken);

        if (isPaused)
        {
            if (_folderSyncCancellations.TryGetValue(folderId, out var activeCts))
            {
                activeCts.Cancel();
            }

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
        var folder = await _repository.GetSyncFolderByIdAsync(folderId, cancellationToken);
        if (folder is null)
        {
            return;
        }

        await _repository.DeleteSyncFolderAsync(folderId, cancellationToken);
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

        var folders = await GetSyncFoldersAsync(cancellationToken);
        foreach (var folder in folders.Where(f => !f.IsPaused))
        {
            await TriggerFolderSyncAsync(folder.Id, cancellationToken);
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
            SyncFoldersChanged?.Invoke(this, EventArgs.Empty);

            var completionMessage = changesCount > 0
                ? $"Sincronização concluída: {latestFolder.LocalPath} ({changesCount} alteração(ões))."
                : $"Sincronização concluída: {latestFolder.LocalPath} (sem alterações).";
            await LogAsync(ActivitySeverity.Success, "SyncCompleted",
                completionMessage,
                latestFolder.Id, latestFolder.LocalPath, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            var latestFolder = await _repository.GetSyncFolderByIdAsync(folderId, CancellationToken.None);
            if (latestFolder is not null)
            {
                latestFolder.State = latestFolder.IsPaused ? FolderSyncState.Paused : FolderSyncState.Idle;
                await _repository.UpsertSyncFolderAsync(latestFolder, CancellationToken.None);
                SyncFoldersChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed for folder '{FolderId}'.", folderId);
            var folder = await _repository.GetSyncFolderByIdAsync(folderId, CancellationToken.None);
            if (folder is not null)
            {
                folder.State = folder.IsPaused ? FolderSyncState.Paused : FolderSyncState.Error;
                folder.LastErrorMessage = ex.Message;
                await _repository.UpsertSyncFolderAsync(folder, CancellationToken.None);
                SyncFoldersChanged?.Invoke(this, EventArgs.Empty);
            }

            await LogAsync(ActivitySeverity.Error, "SyncError",
                $"Erro ao sincronizar pasta: {ex.Message}", folderId, folder?.LocalPath, CancellationToken.None);
        }
        finally
        {
            _folderSyncCancellations.TryRemove(folderId, out _);
            Interlocked.Decrement(ref _activeSyncCount);
            folderLock.Release();
        }
    }

    public async Task<RemoteDriveNode> UploadSingleFileAsync(
        string localFilePath,
        string remoteFolderId = "root",
        CancellationToken cancellationToken = default)
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
        var queueItem = CreateTransferItem(
            syncFolderId: "api-upload",
            fileName: fileInfo.Name,
            relativePath: fileInfo.Name,
            localPath: fullPath,
            remoteFileId: null,
            direction: TransferDirection.Upload,
            totalBytes: fileInfo.Length);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _transferCancellations[queueItem.Id] = linkedCts;

        try
        {
            queueItem.Status = TransferStatus.InProgress;
            PublishTransfer(queueItem);

            var uploaded = await _driveClient.UploadFileAsync(
                fullPath,
                string.IsNullOrWhiteSpace(remoteFolderId) ? "root" : remoteFolderId,
                existingRemoteFileId: null,
                bytes =>
                {
                    queueItem.TransferredBytes = bytes;
                    PublishTransfer(queueItem);
                },
                linkedCts.Token);

            queueItem.RemoteFileId = uploaded.Id;
            queueItem.TransferredBytes = queueItem.TotalBytes;
            queueItem.Status = TransferStatus.Completed;
            queueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
            PublishTransfer(queueItem);

            await LogAsync(ActivitySeverity.Success, "ApiUpload",
                $"Arquivo enviado via API local: {fileInfo.Name}.",
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
                $"Falha no envio via API ({fileInfo.Name}): {ex.Message}",
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

    public async Task<string> DownloadRemoteFileAsync(
        string remoteFileId,
        string fileName,
        long sizeBytes = 0,
        bool isGoogleWorkspaceDoc = false,
        string? mimeType = null,
        CancellationToken cancellationToken = default)
    {
        if (!_driveClient.IsConnected)
        {
            throw new InvalidOperationException("Conta Google Drive não conectada.");
        }

        var downloadDir = _settingsService.Current.DownloadDirectory;
        if (string.IsNullOrWhiteSpace(downloadDir))
        {
            downloadDir = AppPaths.DefaultDownloadDirectory;
        }

        Directory.CreateDirectory(downloadDir);

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
                _ => ("application/pdf", ".pdf")
            };

            if (!safeName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                safeName += ext;
            }
        }

        var targetPath = GetUniqueDestinationPath(downloadDir, safeName);
        var queueItem = CreateTransferItem(
            syncFolderId: "manual-download",
            fileName: Path.GetFileName(targetPath),
            relativePath: Path.GetFileName(targetPath),
            localPath: targetPath,
            remoteFileId: remoteFileId,
            direction: TransferDirection.Download,
            totalBytes: sizeBytes);

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
                await _driveClient.DownloadFileAsync(
                    remoteFileId,
                    targetPath,
                    bytes =>
                    {
                        queueItem.TransferredBytes = bytes;
                        PublishTransfer(queueItem);
                    },
                    linkedCts.Token);
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
            _transferCancellations.TryRemove(queueItem.Id, out _);
        }
    }

    internal static string SanitizeRemoteSegment(string name)
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

    internal static string ResolveSafeLocalPath(string rootDirectory, string relativePath)
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

    private static string GetUniqueDestinationPath(string directory, string fileName)
    {
        var safeFileName = SanitizeRemoteSegment(fileName);
        var candidate = ResolveSafeLocalPath(directory, safeFileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var ext = Path.GetExtension(safeFileName);
        var counter = 1;
        while (File.Exists(candidate))
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
    }

    private async Task<int> SyncFolderInternalAsync(LocalSyncFolder folder, CancellationToken cancellationToken)
    {
        var changesCount = 0;
        if (!Directory.Exists(folder.LocalPath))
        {
            Directory.CreateDirectory(folder.LocalPath);
        }

        var ignorePatterns = _settingsService.Current.IgnorePatterns;
        var trackedEntries = (await _repository.GetTrackedFilesByFolderAsync(folder.Id, cancellationToken))
            .ToDictionary(e => e.RelativePath, StringComparer.Ordinal);

        var localFiles = ScanLocalFiles(folder.LocalPath, ignorePatterns);
        var remoteTree = await ScanRemoteTreeAsync(folder.RemoteFolderId, cancellationToken);

        var remoteFoldersByRelPath = remoteTree.FoldersByRelativePath;
        var remoteFilesByRelPath = remoteTree.FilesByRelativePath;

        foreach (var (relPath, localFile) in localFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            trackedEntries.TryGetValue(relPath, out var tracked);
            remoteFilesByRelPath.TryGetValue(relPath, out var remoteFile);

            var hashes = await FileHashUtility.ComputeHashesAsync(localFile.FullName, cancellationToken);
            var localModUtc = new DateTimeOffset(localFile.LastWriteTimeUtc, TimeSpan.Zero);

            if (remoteFile is not null)
            {
                if (string.Equals(hashes.Md5Hex, remoteFile.Md5Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    await UpsertTrackedAsync(folder.Id, relPath, remoteFile, hashes, localFile.Length, localModUtc, cancellationToken);
                    continue;
                }

                if (folder.Mode == SyncMode.OneWayBackup)
                {
                    await UploadAndTrackAsync(folder, relPath, localFile, remoteFile.ParentIds.FirstOrDefault() ?? folder.RemoteFolderId, remoteFile.Id, hashes, cancellationToken);
                    changesCount++;
                    continue;
                }

                var localChangedSinceTrack = tracked is null ||
                    !string.Equals(tracked.Md5Hash, hashes.Md5Hex, StringComparison.OrdinalIgnoreCase);
                var remoteChangedSinceTrack = tracked is null ||
                    !string.Equals(tracked.Md5Hash, remoteFile.Md5Checksum, StringComparison.OrdinalIgnoreCase);

                if (localChangedSinceTrack && !remoteChangedSinceTrack)
                {
                    await UploadAndTrackAsync(folder, relPath, localFile, remoteFile.ParentIds.FirstOrDefault() ?? folder.RemoteFolderId, remoteFile.Id, hashes, cancellationToken);
                    changesCount++;
                }
                else if (!localChangedSinceTrack && remoteChangedSinceTrack)
                {
                    await DownloadAndTrackAsync(folder, relPath, localFile.FullName, remoteFile, cancellationToken);
                    changesCount++;
                }
                else
                {
                    await ResolveConflictAsync(folder, relPath, localFile, hashes, localModUtc, remoteFile, cancellationToken);
                    changesCount++;
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

                    await _repository.DeleteTrackedFileAsync(tracked.Id, cancellationToken);
                    changesCount++;
                    await LogAsync(ActivitySeverity.Info, "LocalTrashed",
                        $"Arquivo movido para lixeira local após exclusão remota: {relPath}.",
                        folder.Id, relPath, cancellationToken);
                }
                else
                {
                    // Em Backup Unidirecional, mantém o arquivo local sem reenviá-lo após exclusão no Drive,
                    // a menos que o arquivo no PC volte a ser editado localmente.
                    var localModifiedSinceBackup = !string.Equals(tracked.Md5Hash, hashes.Md5Hex, StringComparison.OrdinalIgnoreCase);
                    if (localModifiedSinceBackup)
                    {
                        var parentId = await EnsureRemoteParentFolderAsync(folder.RemoteFolderId, relPath, remoteFoldersByRelPath, cancellationToken);
                        await UploadAndTrackAsync(folder, relPath, localFile, parentId, null, hashes, cancellationToken);
                        changesCount++;
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
                var parentId = await EnsureRemoteParentFolderAsync(folder.RemoteFolderId, relPath, remoteFoldersByRelPath, cancellationToken);
                await UploadAndTrackAsync(folder, relPath, localFile, parentId, null, hashes, cancellationToken);
                changesCount++;
            }
        }

        var trashedAny = false;
        foreach (var (relPath, tracked) in trackedEntries)
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

                // Safe deletion: move to Drive Trash (Trashed = true)
                await _driveClient.TrashFileAsync(remoteFile.Id, cancellationToken);

                deleteQueueItem.TransferredBytes = remoteFile.SizeBytes;
                deleteQueueItem.Status = TransferStatus.Completed;
                deleteQueueItem.CompletedAtUtc = DateTimeOffset.UtcNow;
                PublishTransfer(deleteQueueItem);

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
            foreach (var (relPath, remoteFile) in remoteFilesByRelPath)
            {
                if (localFiles.ContainsKey(relPath) || trackedEntries.ContainsKey(relPath))
                {
                    continue;
                }

                if (SyncIgnoreMatcher.IsIgnored(relPath, ignorePatterns))
                {
                    continue;
                }

                var targetLocalPath = ResolveSafeLocalPath(folder.LocalPath, relPath);
                await DownloadAndTrackAsync(folder, relPath, targetLocalPath, remoteFile, cancellationToken);
                changesCount++;
            }
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
        CancellationToken cancellationToken)
    {
        var queueItem = CreateTransferItem(
            folder.Id,
            localFile.Name,
            relPath,
            localFile.FullName,
            existingRemoteFileId,
            TransferDirection.Upload,
            localFile.Length);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _transferCancellations[queueItem.Id] = linkedCts;

        try
        {
            queueItem.Status = TransferStatus.InProgress;
            PublishTransfer(queueItem);

            var uploaded = await _driveClient.UploadFileAsync(
                localFile.FullName,
                remoteParentId,
                existingRemoteFileId,
                bytes =>
                {
                    queueItem.TransferredBytes = bytes;
                    PublishTransfer(queueItem);
                },
                linkedCts.Token);

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
        CancellationToken cancellationToken)
    {
        var queueItem = CreateTransferItem(
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
            queueItem.Status = TransferStatus.InProgress;
            PublishTransfer(queueItem);

            await _driveClient.DownloadFileAsync(
                remoteFile.Id,
                targetLocalPath,
                bytes =>
                {
                    queueItem.TransferredBytes = bytes;
                    PublishTransfer(queueItem);
                },
                linkedCts.Token);

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

    private async Task<string> EnsureRemoteParentFolderAsync(
        string rootRemoteFolderId,
        string relativeFilePath,
        Dictionary<string, RemoteDriveNode> remoteFoldersByRelPath,
        CancellationToken cancellationToken)
    {
        var dirPart = Path.GetDirectoryName(relativeFilePath)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(dirPart) || dirPart == ".")
        {
            return rootRemoteFolderId;
        }

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

    private static string BuildConflictCopyPath(string originalPath)
    {
        var dir = Path.GetDirectoryName(originalPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(originalPath);
        var ext = Path.GetExtension(originalPath);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(dir, $"{name}.conflito-{stamp}{ext}");
    }

    private static void MoveLocalFileToSystemTrash(string localFilePath)
    {
        if (!File.Exists(localFilePath))
        {
            return;
        }

        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var trashFiles = Path.Combine(home, ".local", "share", "Trash", "files");
            var trashInfo = Path.Combine(home, ".local", "share", "Trash", "info");
            Directory.CreateDirectory(trashFiles);
            Directory.CreateDirectory(trashInfo);

            var fileName = Path.GetFileName(localFilePath);
            var destName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{fileName}";
            var destFile = Path.Combine(trashFiles, destName);
            var infoFile = Path.Combine(trashInfo, $"{destName}.trashinfo");

            var trashMetadata = $"""
                [Trash Info]
                Path={EncodeFreeDesktopTrashPath(localFilePath)}
                DeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}
                """;
            File.WriteAllText(infoFile, trashMetadata);
            File.Move(localFilePath, destFile, overwrite: true);
        }
        catch
        {
            File.Delete(localFilePath);
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
        const int maxRetained = 200;
        if (_transfers.Count <= maxRetained)
        {
            return;
        }

        var overflow = _transfers.Count - maxRetained;
        var candidates = _transfers.Values
            .Where(t => t.Status is TransferStatus.Completed or TransferStatus.Cancelled or TransferStatus.Failed)
            .OrderBy(t => t.CreatedAtUtc)
            .Take(overflow)
            .Select(t => t.Id)
            .ToList();

        foreach (var id in candidates)
        {
            _transfers.TryRemove(id, out _);
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
            _suppressedWatcherFolders.Add(folderId);
        }
    }

    private void ReleaseWatcherSuppressionDelayed(string folderId)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(1800);
            lock (_suppressionLock)
            {
                _suppressedWatcherFolders.Remove(folderId);
            }
        });
    }

    private async Task OnLocalFolderChangedAsync(string folderId)
    {
        lock (_suppressionLock)
        {
            if (_suppressedWatcherFolders.Contains(folderId))
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
