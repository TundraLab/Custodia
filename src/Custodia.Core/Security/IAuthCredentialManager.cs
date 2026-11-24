using Google.Apis.Auth.OAuth2;

namespace TundraDrive.Core.Security;

public sealed record CredentialsFileMetadata(
    string FilePath,
    string? ProjectId,
    string ClientId,
    string MaskedClientId,
    bool IsPerAccount);

public sealed record DetectedCredentialsCandidate(
    string FilePath,
    string FileName,
    string? ProjectId,
    string MaskedClientId,
    DateTimeOffset LastModifiedUtc);

public interface IAuthCredentialManager
{
    string CredentialsFilePath { get; }
    string AccountsDirectoryPath { get; }
    string TokensDirectoryPath { get; }
    string GetAccountFolderPath(string userKey, string? emailAddress = null);
    string GetAccountCredentialsFilePath(string userKey, string? emailAddress = null);
    string ResolveCredentialsFilePath(string? userKey = null, string? emailAddress = null);
    bool CredentialsFileExists(string? userKey = null, string? emailAddress = null);
    CredentialsFileMetadata? InspectCredentialsFile(string? userKey = null, string? emailAddress = null);
    IReadOnlyList<DetectedCredentialsCandidate> FindDownloadsCredentialsCandidates();
    Task OrganizeAccountDirectoryAsync(string userKey, string? emailAddress, string? displayName = null, CancellationToken cancellationToken = default);
    Task ImportCredentialsFileAsync(string sourceJsonPath, string? userKey = null, string? emailAddress = null, CancellationToken cancellationToken = default);
    Task CopyCredentialsAsync(string sourceUserKey, string targetUserKey, string? targetEmailAddress = null, CancellationToken cancellationToken = default);
    void DeleteAccountCredentialsFile(string userKey, string? emailAddress = null);
    Task<bool> HasStoredTokenAsync(string userKey = "default");
    Task<UserCredential> AuthorizeInteractiveAsync(string userKey = "default", CancellationToken cancellationToken = default);
    Task<UserCredential?> TryRestoreSilentAsync(string userKey = "default", CancellationToken cancellationToken = default);
    Task CopyTokenAsync(string sourceUserKey, string targetUserKey, CancellationToken cancellationToken = default);
    Task DeleteLocalTokenAsync(string userKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TundraDrive.Core.Models.LocalSyncFolder>> LoadAccountSyncFoldersProfileAsync(string userKey, string? emailAddress, CancellationToken cancellationToken = default);
    Task SaveAccountSyncFoldersProfileAsync(string userKey, string? emailAddress, IReadOnlyList<TundraDrive.Core.Models.LocalSyncFolder> folders, CancellationToken cancellationToken = default);
    Task DeleteAccountSyncFoldersProfileAsync(string userKey, string? emailAddress, CancellationToken cancellationToken = default);
    Task RevokeAndClearAsync(string userKey = "default", string? emailAddress = null, bool purgeFolderConfig = false, CancellationToken cancellationToken = default);
    Task ClearAllLocalTokensAsync(CancellationToken cancellationToken = default);
    void DeleteCredentialsFile();
}
