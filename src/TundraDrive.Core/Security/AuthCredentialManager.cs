using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Logging;
using TundraDrive.Core.Configuration;

namespace TundraDrive.Core.Security;

public sealed class AuthCredentialManager : IAuthCredentialManager
{
    private static readonly string[] DriveScopes = [DriveService.Scope.Drive];
    private readonly ILogger<AuthCredentialManager> _logger;
    private readonly SecureFileDataStore _tokenStore;

    public AuthCredentialManager(ILogger<AuthCredentialManager> logger)
    {
        _logger = logger;
        PosixPermissionHelper.EnsurePrivateDirectory(AppPaths.ConfigDirectory);
        PosixPermissionHelper.EnsurePrivateDirectory(AppPaths.DataDirectory);
        _tokenStore = new SecureFileDataStore(AppPaths.TokensDirectory);
    }

    public string CredentialsFilePath => AppPaths.CredentialsFilePath;
    public string AccountsCredentialsDirectoryPath => Path.Combine(AppPaths.ConfigDirectory, "accounts-credentials");
    public string TokensDirectoryPath => AppPaths.TokensDirectory;

    public string GetAccountCredentialsFilePath(string userKey)
    {
        var safeKey = string.Concat(
            (string.IsNullOrWhiteSpace(userKey) ? "default" : userKey)
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return Path.Combine(AccountsCredentialsDirectoryPath, $"{safeKey}.json");
    }

    public string ResolveCredentialsFilePath(string? userKey = null)
    {
        if (!string.IsNullOrWhiteSpace(userKey))
        {
            var accountSpecific = GetAccountCredentialsFilePath(userKey);
            if (File.Exists(accountSpecific))
            {
                return accountSpecific;
            }
        }

        return CredentialsFilePath;
    }

    public bool CredentialsFileExists(string? userKey = null) =>
        File.Exists(ResolveCredentialsFilePath(userKey));

    public async Task ImportCredentialsFileAsync(
        string sourceJsonPath,
        string? userKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceJsonPath) || !File.Exists(sourceJsonPath))
        {
            throw new FileNotFoundException("Selected credentials file was not found.", sourceJsonPath);
        }

        await using (var validationStream = File.OpenRead(sourceJsonPath))
        {
            using var jsonDoc = await System.Text.Json.JsonDocument.ParseAsync(validationStream, cancellationToken: cancellationToken);
            var root = jsonDoc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
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

        await using (var secretsStream = File.OpenRead(sourceJsonPath))
        {
            var secrets = await GoogleClientSecrets.FromStreamAsync(secretsStream, cancellationToken);
            if (secrets?.Secrets is null || string.IsNullOrWhiteSpace(secrets.Secrets.ClientId))
            {
                throw new InvalidDataException("Invalid Google OAuth2 client credentials JSON.");
            }
        }

        PosixPermissionHelper.EnsurePrivateDirectory(AppPaths.ConfigDirectory);

        if (string.IsNullOrWhiteSpace(userKey))
        {
            File.Copy(sourceJsonPath, CredentialsFilePath, overwrite: true);
            PosixPermissionHelper.EnsureFileMode0600(CredentialsFilePath);
            _logger.LogInformation("Imported global OAuth2 credentials file into '{Path}' with mode 0600.", CredentialsFilePath);
            return;
        }

        PosixPermissionHelper.EnsurePrivateDirectory(AccountsCredentialsDirectoryPath);
        var accountPath = GetAccountCredentialsFilePath(userKey);
        File.Copy(sourceJsonPath, accountPath, overwrite: true);
        PosixPermissionHelper.EnsureFileMode0600(accountPath);

        if (!File.Exists(CredentialsFilePath))
        {
            File.Copy(sourceJsonPath, CredentialsFilePath, overwrite: true);
            PosixPermissionHelper.EnsureFileMode0600(CredentialsFilePath);
        }

        _logger.LogInformation("Imported per-account OAuth2 credentials file into '{Path}' with mode 0600.", accountPath);
    }

    public Task CopyCredentialsAsync(
        string sourceUserKey,
        string targetUserKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var src = GetAccountCredentialsFilePath(sourceUserKey);
        if (!File.Exists(src))
        {
            return Task.CompletedTask;
        }

        PosixPermissionHelper.EnsurePrivateDirectory(AccountsCredentialsDirectoryPath);
        var dst = GetAccountCredentialsFilePath(targetUserKey);
        File.Copy(src, dst, overwrite: true);
        PosixPermissionHelper.EnsureFileMode0600(dst);
        return Task.CompletedTask;
    }

    public void DeleteAccountCredentialsFile(string userKey)
    {
        if (string.IsNullOrWhiteSpace(userKey))
        {
            return;
        }

        var path = GetAccountCredentialsFilePath(userKey);
        if (File.Exists(path))
        {
            File.Delete(path);
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

    public async Task RevokeAndClearAsync(
        string userKey = "default",
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
            DeleteAccountCredentialsFile(userKey);
            PosixPermissionHelper.EnforcePrivateDirectoryFiles0600(TokensDirectoryPath);
        }
    }

    public async Task ClearAllLocalTokensAsync(CancellationToken cancellationToken = default)
    {
        await _tokenStore.ClearAsync();
        if (Directory.Exists(AccountsCredentialsDirectoryPath))
        {
            Directory.Delete(AccountsCredentialsDirectoryPath, recursive: true);
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
}
