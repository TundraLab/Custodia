using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Logging;
using TundraDrive.Core.Configuration;

namespace TundraDrive.Core.Security;

public sealed class AuthCredentialManager : IAuthCredentialManager
{
    private const string AccountMetaFileName = "conta.json";
    private const string PerAccountCredentialsFileName = "credentials.json";
    private const string PerAccountFoldersFileName = "pastas-sincronizadas.json";

    private sealed record SavedSyncFolderEntry(
        string LocalPath,
        string RemoteFolderId,
        string RemoteFolderName,
        TundraDrive.Core.Models.SyncMode Mode,
        bool IsPaused,
        string? CustomAlias,
        string? CustomColorHex,
        string? CustomIconKey);

    private static readonly string[] DriveScopes =
    [
        DriveService.Scope.Drive,
        "https://www.googleapis.com/auth/gmail.send"
    ];

    private sealed record AccountFolderMeta(
        string AccountId,
        string? EmailAddress,
        string? DisplayName,
        DateTimeOffset UpdatedAtUtc);

    private readonly ILogger<AuthCredentialManager> _logger;
    private readonly SecureFileDataStore _tokenStore;

    public AuthCredentialManager(ILogger<AuthCredentialManager> logger)
    {
        _logger = logger;
        PosixPermissionHelper.EnsurePrivateDirectory(AppPaths.ConfigDirectory);
        PosixPermissionHelper.EnsurePrivateDirectory(AppPaths.AccountsDirectory);
        PosixPermissionHelper.EnsurePrivateDirectory(AppPaths.DataDirectory);
        _tokenStore = new SecureFileDataStore(AppPaths.TokensDirectory);
        MigrateLegacyCredentialsDirectory();
    }

    public string CredentialsFilePath => AppPaths.CredentialsFilePath;
    public string AccountsDirectoryPath => AppPaths.AccountsDirectory;
    public string LegacyAccountsCredentialsDirectoryPath => Path.Combine(AppPaths.ConfigDirectory, "accounts-credentials");
    public string TokensDirectoryPath => AppPaths.TokensDirectory;

    public string GetAccountFolderPath(string userKey, string? emailAddress = null)
    {
        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            return Path.Combine(AccountsDirectoryPath, SanitizeEmailFolderName(emailAddress));
        }

        var mappedDir = FindAccountDirectoryById(userKey);
        if (!string.IsNullOrWhiteSpace(mappedDir))
        {
            return mappedDir;
        }

        return Path.Combine(AccountsDirectoryPath, SanitizeKeySegment(userKey));
    }

    public string GetAccountCredentialsFilePath(string userKey, string? emailAddress = null) =>
        Path.Combine(GetAccountFolderPath(userKey, emailAddress), PerAccountCredentialsFileName);

    public string ResolveCredentialsFilePath(string? userKey = null, string? emailAddress = null)
    {
        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            var emailPath = Path.Combine(
                AccountsDirectoryPath,
                SanitizeEmailFolderName(emailAddress),
                PerAccountCredentialsFileName);
            if (File.Exists(emailPath))
            {
                return emailPath;
            }
        }

        if (!string.IsNullOrWhiteSpace(userKey))
        {
            var mappedDir = FindAccountDirectoryById(userKey);
            if (!string.IsNullOrWhiteSpace(mappedDir))
            {
                var mappedCreds = Path.Combine(mappedDir, PerAccountCredentialsFileName);
                if (File.Exists(mappedCreds))
                {
                    return mappedCreds;
                }
            }

            var keyPath = Path.Combine(
                AccountsDirectoryPath,
                SanitizeKeySegment(userKey),
                PerAccountCredentialsFileName);
            if (File.Exists(keyPath))
            {
                return keyPath;
            }

            var legacyPath = Path.Combine(
                LegacyAccountsCredentialsDirectoryPath,
                $"{SanitizeKeySegment(userKey)}.json");
            if (File.Exists(legacyPath))
            {
                return legacyPath;
            }
        }

        return CredentialsFilePath;
    }

    public bool CredentialsFileExists(string? userKey = null, string? emailAddress = null) =>
        File.Exists(ResolveCredentialsFilePath(userKey, emailAddress));

    public CredentialsFileMetadata? InspectCredentialsFile(string? userKey = null, string? emailAddress = null)
    {
        var resolvedPath = ResolveCredentialsFilePath(userKey, emailAddress);
        if (!File.Exists(resolvedPath))
        {
            return null;
        }

        var isPerAccount = !string.Equals(
            Path.GetFullPath(resolvedPath),
            Path.GetFullPath(CredentialsFilePath),
            StringComparison.Ordinal);

        return TryParseCredentialsMetadata(resolvedPath, isPerAccount);
    }

    public IReadOnlyList<DetectedCredentialsCandidate> FindDownloadsCredentialsCandidates() =>
        FindCredentialsCandidatesInDirectory(AppPaths.DefaultDownloadDirectory);

    internal IReadOnlyList<DetectedCredentialsCandidate> FindCredentialsCandidatesInDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return [];
        }

        var results = new List<DetectedCredentialsCandidate>();
        try
        {
            var candidates = Directory
                .EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return name.StartsWith("client_secret", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("credentials", StringComparison.OrdinalIgnoreCase);
                })
                .Select(path => new FileInfo(path))
                .Where(fi => fi.Exists && fi.Length is > 32 and <= 262_144)
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .Take(10);

            foreach (var fi in candidates)
            {
                var meta = TryParseCredentialsMetadata(fi.FullName, isPerAccount: false);
                if (meta is null)
                {
                    continue;
                }

                results.Add(new DetectedCredentialsCandidate(
                    fi.FullName,
                    fi.Name,
                    meta.ProjectId,
                    meta.MaskedClientId,
                    new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero)));

                if (results.Count >= 3)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not scan Downloads directory for OAuth credentials.");
        }

        return results;
    }

    public async Task OrganizeAccountDirectoryAsync(
        string userKey,
        string? emailAddress,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userKey))
        {
            return;
        }

        PosixPermissionHelper.EnsurePrivateDirectory(AccountsDirectoryPath);
        var targetFolder = GetAccountFolderPath(userKey, emailAddress);
        PosixPermissionHelper.EnsurePrivateDirectory(targetFolder);

        var targetCreds = Path.Combine(targetFolder, PerAccountCredentialsFileName);
        if (!File.Exists(targetCreds))
        {
            var keyCreds = Path.Combine(
                AccountsDirectoryPath,
                SanitizeKeySegment(userKey),
                PerAccountCredentialsFileName);
            var legacyCreds = Path.Combine(
                LegacyAccountsCredentialsDirectoryPath,
                $"{SanitizeKeySegment(userKey)}.json");

            if (File.Exists(keyCreds) &&
                !string.Equals(Path.GetFullPath(keyCreds), Path.GetFullPath(targetCreds), StringComparison.Ordinal))
            {
                File.Move(keyCreds, targetCreds, overwrite: true);
                PosixPermissionHelper.EnsureFileMode0600(targetCreds);
                TryDeleteEmptyDirectory(Path.GetDirectoryName(keyCreds));
            }
            else if (File.Exists(legacyCreds))
            {
                File.Move(legacyCreds, targetCreds, overwrite: true);
                PosixPermissionHelper.EnsureFileMode0600(targetCreds);
            }
        }

        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            var oldKeyDir = Path.Combine(AccountsDirectoryPath, SanitizeKeySegment(userKey));
            if (Directory.Exists(oldKeyDir) &&
                !string.Equals(Path.GetFullPath(oldKeyDir), Path.GetFullPath(targetFolder), StringComparison.Ordinal))
            {
                TryDeleteAccountMetaAndFolderIfObsolete(oldKeyDir);
            }
        }

        await WriteAccountMetaAsync(targetFolder, userKey, emailAddress, displayName, cancellationToken);
    }

    public async Task ImportCredentialsFileAsync(
        string sourceJsonPath,
        string? userKey = null,
        string? emailAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceJsonPath) || !File.Exists(sourceJsonPath))
        {
            throw new FileNotFoundException("Selected credentials file was not found.", sourceJsonPath);
        }

        await ValidateDesktopOAuthJsonAsync(sourceJsonPath, cancellationToken);

        PosixPermissionHelper.EnsurePrivateDirectory(AppPaths.ConfigDirectory);
        PosixPermissionHelper.EnsurePrivateDirectory(AccountsDirectoryPath);

        if (string.IsNullOrWhiteSpace(userKey))
        {
            File.Copy(sourceJsonPath, CredentialsFilePath, overwrite: true);
            PosixPermissionHelper.EnsureFileMode0600(CredentialsFilePath);
            _logger.LogInformation("Imported global OAuth2 credentials file into '{Path}' with mode 0600.", CredentialsFilePath);
            return;
        }

        var accountFolder = GetAccountFolderPath(userKey, emailAddress);
        PosixPermissionHelper.EnsurePrivateDirectory(accountFolder);

        var accountCredsPath = Path.Combine(accountFolder, PerAccountCredentialsFileName);
        File.Copy(sourceJsonPath, accountCredsPath, overwrite: true);
        PosixPermissionHelper.EnsureFileMode0600(accountCredsPath);
        await WriteAccountMetaAsync(accountFolder, userKey, emailAddress, null, cancellationToken);

        if (!File.Exists(CredentialsFilePath))
        {
            File.Copy(sourceJsonPath, CredentialsFilePath, overwrite: true);
            PosixPermissionHelper.EnsureFileMode0600(CredentialsFilePath);
        }

        _logger.LogInformation("Imported per-account OAuth2 credentials file into '{Path}' with mode 0600.", accountCredsPath);
    }

    public Task CopyCredentialsAsync(
        string sourceUserKey,
        string targetUserKey,
        string? targetEmailAddress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var src = ResolveAccountOnlyCredentialsFilePath(sourceUserKey, null);
        if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
        {
            return Task.CompletedTask;
        }

        PosixPermissionHelper.EnsurePrivateDirectory(AccountsDirectoryPath);
        var targetDir = GetAccountFolderPath(targetUserKey, targetEmailAddress);
        PosixPermissionHelper.EnsurePrivateDirectory(targetDir);

        var dst = Path.Combine(targetDir, PerAccountCredentialsFileName);
        if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.Ordinal))
        {
            File.Copy(src, dst, overwrite: true);
            PosixPermissionHelper.EnsureFileMode0600(dst);
        }

        return Task.CompletedTask;
    }

    public void DeleteAccountCredentialsFile(string userKey, string? emailAddress = null)
    {
        if (string.IsNullOrWhiteSpace(userKey))
        {
            return;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            paths.Add(Path.Combine(AccountsDirectoryPath, SanitizeEmailFolderName(emailAddress), PerAccountCredentialsFileName));
        }

        var mappedDir = FindAccountDirectoryById(userKey);
        if (!string.IsNullOrWhiteSpace(mappedDir))
        {
            paths.Add(Path.Combine(mappedDir, PerAccountCredentialsFileName));
        }

        paths.Add(Path.Combine(AccountsDirectoryPath, SanitizeKeySegment(userKey), PerAccountCredentialsFileName));
        paths.Add(Path.Combine(LegacyAccountsCredentialsDirectoryPath, $"{SanitizeKeySegment(userKey)}.json"));

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public async Task<bool> HasStoredTokenAsync(string userKey = "default")
    {
        var token = await _tokenStore.GetAsync<TokenResponse>(userKey);
        return token is not null && !string.IsNullOrWhiteSpace(token.RefreshToken);
    }

    public async Task<UserCredential> AuthorizeInteractiveAsync(
        string userKey = "default",
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = ResolveCredentialsFilePath(userKey);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                $"OAuth credentials file not found at '{resolvedPath}'.",
                resolvedPath);
        }

        PosixPermissionHelper.EnsureFileMode0600(resolvedPath);

        await using var stream = File.OpenRead(resolvedPath);
        var clientSecrets = (await GoogleClientSecrets.FromStreamAsync(stream, cancellationToken)).Secrets;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            DriveScopes,
            userKey,
            timeoutCts.Token,
            _tokenStore);

        PosixPermissionHelper.EnforcePrivateDirectoryFiles0600(TokensDirectoryPath);
        _logger.LogInformation("Google OAuth2 authorization succeeded for user key '{UserKey}'.", userKey);
        return credential;
    }

    public async Task<UserCredential?> TryRestoreSilentAsync(
        string userKey = "default",
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = ResolveCredentialsFilePath(userKey);
        if (!File.Exists(resolvedPath))
        {
            _logger.LogWarning("Credentials file missing at '{Path}'. Skipping silent auth restore.", resolvedPath);
            return null;
        }

        if (!await HasStoredTokenAsync(userKey))
        {
            return null;
        }

        try
        {
            return await AuthorizeInteractiveAsync(userKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to silently restore Google OAuth2 session.");
            return null;
        }
    }

    public async Task CopyTokenAsync(
        string sourceUserKey,
        string targetUserKey,
        CancellationToken cancellationToken = default)
    {
        var token = await _tokenStore.GetAsync<TokenResponse>(sourceUserKey);
        if (token is not null)
        {
            await _tokenStore.StoreAsync(targetUserKey, token);
            PosixPermissionHelper.EnforcePrivateDirectoryFiles0600(TokensDirectoryPath);
        }
    }

    public async Task DeleteLocalTokenAsync(
        string userKey,
        CancellationToken cancellationToken = default)
    {
        await _tokenStore.DeleteAsync<TokenResponse>(userKey);
        PosixPermissionHelper.EnforcePrivateDirectoryFiles0600(TokensDirectoryPath);
    }

    public async Task<IReadOnlyList<TundraDrive.Core.Models.LocalSyncFolder>> LoadAccountSyncFoldersProfileAsync(
        string userKey,
        string? emailAddress,
        CancellationToken cancellationToken = default)
    {
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            dirs.Add(Path.Combine(AccountsDirectoryPath, SanitizeEmailFolderName(emailAddress)));
        }

        var mappedDir = FindAccountDirectoryById(userKey);
        if (!string.IsNullOrWhiteSpace(mappedDir) && !dirs.Contains(mappedDir, StringComparer.Ordinal))
        {
            dirs.Add(mappedDir);
        }

        var byKeyDir = Path.Combine(AccountsDirectoryPath, SanitizeKeySegment(userKey));
        if (!dirs.Contains(byKeyDir, StringComparer.Ordinal))
        {
            dirs.Add(byKeyDir);
        }

        foreach (var dir in dirs)
        {
            var profilePath = Path.Combine(dir, PerAccountFoldersFileName);
            if (!File.Exists(profilePath))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(profilePath, cancellationToken);
                var entries = JsonSerializer.Deserialize<List<SavedSyncFolderEntry>>(json);
                if (entries is null || entries.Count == 0)
                {
                    return [];
                }

                return entries
                    .Where(e => !string.IsNullOrWhiteSpace(e.LocalPath))
                    .Select(e => new TundraDrive.Core.Models.LocalSyncFolder
                    {
                        AccountId = userKey,
                        AccountEmail = emailAddress?.Trim().ToLowerInvariant(),
                        LocalPath = e.LocalPath,
                        RemoteFolderId = string.IsNullOrWhiteSpace(e.RemoteFolderId) ? "root" : e.RemoteFolderId,
                        RemoteFolderName = string.IsNullOrWhiteSpace(e.RemoteFolderName) ? "Meus Arquivos" : e.RemoteFolderName,
                        Mode = e.Mode,
                        IsPaused = e.IsPaused,
                        State = e.IsPaused ? TundraDrive.Core.Models.FolderSyncState.Paused : TundraDrive.Core.Models.FolderSyncState.Idle,
                        CustomAlias = e.CustomAlias,
                        CustomColorHex = e.CustomColorHex,
                        CustomIconKey = e.CustomIconKey
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read sync folder profile at '{Path}'.", profilePath);
            }
        }

        return [];
    }

    public async Task SaveAccountSyncFoldersProfileAsync(
        string userKey,
        string? emailAddress,
        IReadOnlyList<TundraDrive.Core.Models.LocalSyncFolder> folders,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userKey) && string.IsNullOrWhiteSpace(emailAddress))
        {
            return;
        }

        var folderPath = GetAccountFolderPath(userKey, emailAddress);
        PosixPermissionHelper.EnsurePrivateDirectory(folderPath);
        var profilePath = Path.Combine(folderPath, PerAccountFoldersFileName);

        var entries = folders.Select(f => new SavedSyncFolderEntry(
            f.LocalPath,
            f.RemoteFolderId,
            f.RemoteFolderName,
            f.Mode,
            f.IsPaused,
            f.CustomAlias,
            f.CustomColorHex,
            f.CustomIconKey)).ToList();

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(profilePath, json, cancellationToken);
        PosixPermissionHelper.EnsureFileMode0600(profilePath);
    }

    public Task DeleteAccountSyncFoldersProfileAsync(
        string userKey,
        string? emailAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var dir in CollectCandidateAccountDirs(userKey, emailAddress))
        {
            var profilePath = Path.Combine(dir, PerAccountFoldersFileName);
            if (File.Exists(profilePath))
            {
                File.Delete(profilePath);
            }

            TryDeleteAccountMetaAndFolderIfObsolete(dir);
        }

        return Task.CompletedTask;
    }

    public async Task RevokeAndClearAsync(
        string userKey = "default",
        string? emailAddress = null,
        bool purgeFolderConfig = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await _tokenStore.GetAsync<TokenResponse>(userKey);
            var revokable = !string.IsNullOrWhiteSpace(token?.RefreshToken)
                ? token.RefreshToken
                : token?.AccessToken;

            if (!string.IsNullOrWhiteSpace(revokable))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("token", revokable)]);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(3));
                await http.PostAsync("https://oauth2.googleapis.com/revoke", content, cts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token revocation request failed; clearing local token store anyway.");
        }
        finally
        {
            await _tokenStore.DeleteAsync<TokenResponse>(userKey);
            DeleteAccountCredentialsFile(userKey, emailAddress);

            if (purgeFolderConfig)
            {
                await DeleteAccountSyncFoldersProfileAsync(userKey, emailAddress, cancellationToken);
            }
            else
            {
                foreach (var dir in CollectCandidateAccountDirs(userKey, emailAddress))
                {
                    TryDeleteAccountMetaAndFolderIfObsolete(dir);
                }
            }

            PosixPermissionHelper.EnforcePrivateDirectoryFiles0600(TokensDirectoryPath);
        }
    }

    private IEnumerable<string> CollectCandidateAccountDirs(string userKey, string? emailAddress)
    {
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            dirs.Add(Path.Combine(AccountsDirectoryPath, SanitizeEmailFolderName(emailAddress)));
        }

        var dirById = FindAccountDirectoryById(userKey);
        if (!string.IsNullOrWhiteSpace(dirById))
        {
            dirs.Add(dirById);
        }

        if (!string.IsNullOrWhiteSpace(userKey))
        {
            dirs.Add(Path.Combine(AccountsDirectoryPath, SanitizeKeySegment(userKey)));
        }

        return dirs.Distinct(StringComparer.Ordinal);
    }

    public async Task ClearAllLocalTokensAsync(CancellationToken cancellationToken = default)
    {
        await _tokenStore.ClearAsync();
        if (Directory.Exists(AccountsDirectoryPath))
        {
            Directory.Delete(AccountsDirectoryPath, recursive: true);
        }
        if (Directory.Exists(LegacyAccountsCredentialsDirectoryPath))
        {
            Directory.Delete(LegacyAccountsCredentialsDirectoryPath, recursive: true);
        }
        PosixPermissionHelper.EnforcePrivateDirectoryFiles0600(TokensDirectoryPath);
    }

    public void DeleteCredentialsFile()
    {
        if (File.Exists(CredentialsFilePath))
        {
            File.Delete(CredentialsFilePath);
            _logger.LogInformation("Deleted OAuth2 credentials file at '{Path}'.", CredentialsFilePath);
        }
    }

    private string? ResolveAccountOnlyCredentialsFilePath(string userKey, string? emailAddress)
    {
        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            var p = Path.Combine(AccountsDirectoryPath, SanitizeEmailFolderName(emailAddress), PerAccountCredentialsFileName);
            if (File.Exists(p))
            {
                return p;
            }
        }

        var mappedDir = FindAccountDirectoryById(userKey);
        if (!string.IsNullOrWhiteSpace(mappedDir))
        {
            var p = Path.Combine(mappedDir, PerAccountCredentialsFileName);
            if (File.Exists(p))
            {
                return p;
            }
        }

        var byKey = Path.Combine(AccountsDirectoryPath, SanitizeKeySegment(userKey), PerAccountCredentialsFileName);
        if (File.Exists(byKey))
        {
            return byKey;
        }

        var legacy = Path.Combine(LegacyAccountsCredentialsDirectoryPath, $"{SanitizeKeySegment(userKey)}.json");
        return File.Exists(legacy) ? legacy : null;
    }

    private void MigrateLegacyCredentialsDirectory()
    {
        try
        {
            if (!Directory.Exists(LegacyAccountsCredentialsDirectoryPath))
            {
                return;
            }

            foreach (var jsonFile in Directory.EnumerateFiles(LegacyAccountsCredentialsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                var key = Path.GetFileNameWithoutExtension(jsonFile);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var targetDir = Path.Combine(AccountsDirectoryPath, SanitizeKeySegment(key));
                PosixPermissionHelper.EnsurePrivateDirectory(targetDir);
                var targetFile = Path.Combine(targetDir, PerAccountCredentialsFileName);
                if (!File.Exists(targetFile))
                {
                    File.Move(jsonFile, targetFile, overwrite: true);
                    PosixPermissionHelper.EnsureFileMode0600(targetFile);
                }
                else
                {
                    File.Delete(jsonFile);
                }
            }

            TryDeleteEmptyDirectory(LegacyAccountsCredentialsDirectoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Legacy per-account credentials migration skipped.");
        }
    }

    private string? FindAccountDirectoryById(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId) || !Directory.Exists(AccountsDirectoryPath))
        {
            return null;
        }

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(AccountsDirectoryPath))
            {
                var metaPath = Path.Combine(dir, AccountMetaFileName);
                if (!File.Exists(metaPath))
                {
                    continue;
                }

                var json = File.ReadAllText(metaPath);
                var meta = JsonSerializer.Deserialize<AccountFolderMeta>(json);
                if (meta is not null && string.Equals(meta.AccountId, accountId, StringComparison.Ordinal))
                {
                    return dir;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static async Task WriteAccountMetaAsync(
        string folderPath,
        string accountId,
        string? emailAddress,
        string? displayName,
        CancellationToken cancellationToken)
    {
        try
        {
            var metaPath = Path.Combine(folderPath, AccountMetaFileName);
            AccountFolderMeta? existing = null;
            if (File.Exists(metaPath))
            {
                try
                {
                    existing = JsonSerializer.Deserialize<AccountFolderMeta>(await File.ReadAllTextAsync(metaPath, cancellationToken));
                }
                catch
                {
                }
            }

            var meta = new AccountFolderMeta(
                accountId,
                string.IsNullOrWhiteSpace(emailAddress) ? existing?.EmailAddress : emailAddress,
                string.IsNullOrWhiteSpace(displayName) ? existing?.DisplayName : displayName,
                DateTimeOffset.UtcNow);

            var payload = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metaPath, payload, cancellationToken);
            PosixPermissionHelper.EnsureFileMode0600(metaPath);
        }
        catch
        {
        }
    }

    private static async Task ValidateDesktopOAuthJsonAsync(string sourceJsonPath, CancellationToken cancellationToken)
    {
        await using (var validationStream = File.OpenRead(sourceJsonPath))
        {
            using var jsonDoc = await JsonDocument.ParseAsync(validationStream, cancellationToken: cancellationToken);
            var root = jsonDoc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Invalid Google OAuth2 client credentials JSON.");
            }

            if (root.TryGetProperty("web", out _) && !root.TryGetProperty("installed", out _))
            {
                throw new InvalidDataException("OAuth2 credentials of type 'web' are not supported. Create a Desktop ('installed') OAuth 2.0 Client ID in Google Cloud Console.");
            }

            if (!root.TryGetProperty("installed", out _))
            {
                throw new InvalidDataException("Google OAuth2 JSON must contain an 'installed' Desktop client configuration.");
            }
        }

        await using var secretsStream = File.OpenRead(sourceJsonPath);
        var secrets = await GoogleClientSecrets.FromStreamAsync(secretsStream, cancellationToken);
        if (secrets?.Secrets is null || string.IsNullOrWhiteSpace(secrets.Secrets.ClientId))
        {
            throw new InvalidDataException("Invalid Google OAuth2 client credentials JSON.");
        }
    }

    private static CredentialsFileMetadata? TryParseCredentialsMetadata(string filePath, bool isPerAccount)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("installed", out var installed) ||
                installed.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!installed.TryGetProperty("client_id", out var clientIdProp))
            {
                return null;
            }

            var clientId = clientIdProp.GetString();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return null;
            }

            string? projectId = null;
            if (installed.TryGetProperty("project_id", out var projectIdProp))
            {
                projectId = projectIdProp.GetString();
            }

            return new CredentialsFileMetadata(
                filePath,
                string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim(),
                clientId.Trim(),
                MaskClientId(clientId.Trim()),
                isPerAccount);
        }
        catch
        {
            return null;
        }
    }

    private static string MaskClientId(string clientId)
    {
        const string googleSuffix = ".apps.googleusercontent.com";
        var suffixIdx = clientId.IndexOf(googleSuffix, StringComparison.OrdinalIgnoreCase);
        if (suffixIdx > 20)
        {
            var head = clientId[..Math.Min(16, suffixIdx)];
            var tailStart = Math.Max(0, suffixIdx - 6);
            return $"{head}...{clientId[tailStart..]}";
        }

        if (clientId.Length <= 24)
        {
            return clientId;
        }

        return $"{clientId[..10]}...{clientId[^16..]}";
    }

    private static string SanitizeEmailFolderName(string emailAddress)
    {
        var trimmed = emailAddress.Trim().ToLowerInvariant();
        var cleaned = string.Concat(trimmed.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '@' or '.' or '-' or '_' ? ch : '_'));
        return string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or ".."
            ? "account"
            : cleaned;
    }

    private static string SanitizeKeySegment(string? userKey)
    {
        var raw = string.IsNullOrWhiteSpace(userKey) ? "default" : userKey.Trim();
        var cleaned = string.Concat(raw.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or ".."
            ? "default"
            : cleaned;
    }

    private static void TryDeleteAccountMetaAndFolderIfObsolete(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            var metaPath = Path.Combine(directoryPath, AccountMetaFileName);
            var credsPath = Path.Combine(directoryPath, PerAccountCredentialsFileName);
            var foldersPath = Path.Combine(directoryPath, PerAccountFoldersFileName);
            if (!File.Exists(credsPath) && !File.Exists(foldersPath) && File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }

            TryDeleteEmptyDirectory(directoryPath);
        }
        catch
        {
        }
    }

    private static void TryDeleteEmptyDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath, recursive: false);
            }
        }
        catch
        {
        }
    }
}
