using Google.Apis.Auth.OAuth2;

namespace TundraDrive.Core.Security;

public interface IAuthCredentialManager
{
    string CredentialsFilePath { get; }
    string TokensDirectoryPath { get; }
    bool CredentialsFileExists(string? userKey = null);
    Task ImportCredentialsFileAsync(string sourceJsonPath, string? userKey = null, CancellationToken cancellationToken = default);
    Task CopyCredentialsAsync(string sourceUserKey, string targetUserKey, CancellationToken cancellationToken = default);
    void DeleteAccountCredentialsFile(string userKey);
    Task<bool> HasStoredTokenAsync(string userKey = "default");
    Task<UserCredential> AuthorizeInteractiveAsync(string userKey = "default", CancellationToken cancellationToken = default);
    Task<UserCredential?> TryRestoreSilentAsync(string userKey = "default", CancellationToken cancellationToken = default);
    Task CopyTokenAsync(string sourceUserKey, string targetUserKey, CancellationToken cancellationToken = default);
    Task DeleteLocalTokenAsync(string userKey, CancellationToken cancellationToken = default);
    Task RevokeAndClearAsync(string userKey = "default", CancellationToken cancellationToken = default);
    Task ClearAllLocalTokensAsync(CancellationToken cancellationToken = default);
    void DeleteCredentialsFile();
}
