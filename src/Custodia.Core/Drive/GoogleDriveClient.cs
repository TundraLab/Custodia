using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.Extensions.Logging;
using TundraDrive.Core.Models;
using TundraDrive.Core.Security;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace TundraDrive.Core.Drive;

public sealed class GoogleDriveClient : IGoogleDriveClient, IDisposable
{
    private const string FileFields = "id, name, mimeType, size, md5Checksum, modifiedTime, parents, trashed";
    private const string ListFields = $"nextPageToken, files({FileFields})";

    private readonly IAuthCredentialManager _authManager;
    private readonly ILogger<GoogleDriveClient> _logger;
    private readonly SemaphoreSlim _serviceGate = new(1, 1);

    private DriveService? _driveService;
    private UserCredential? _activeCredential;
    private string _activeAccountKey = "default";

    public GoogleDriveClient(
        IAuthCredentialManager authManager,
        ILogger<GoogleDriveClient> logger)
    {
        _authManager = authManager;
        _logger = logger;
    }

    public bool IsConnected => _driveService is not null;
    public string ActiveAccountKey => _activeAccountKey;

    public async Task<DriveAccount> ConnectInteractiveAsync(string userKey = "default", CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrWhiteSpace(userKey) ? "default" : userKey;
        var credential = await _authManager.AuthorizeInteractiveAsync(key, cancellationToken);
        await InitializeDriveServiceAsync(credential, key, cancellationToken);
        return await GetAccountInfoAsync(cancellationToken);
    }

    public async Task<DriveAccount?> TryRestoreSessionAsync(string userKey = "default", CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrWhiteSpace(userKey) ? "default" : userKey;
        var credential = await _authManager.TryRestoreSilentAsync(key, cancellationToken);
        if (credential is null)
        {
            return null;
        }

        await InitializeDriveServiceAsync(credential, key, cancellationToken);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            return await GetAccountInfoAsync(cts.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Remote account metadata unavailable during startup restore for '{UserKey}'; using local session.", key);
            return new DriveAccount
            {
                Id = key,
                DisplayName = "Conta Google",
                EmailAddress = string.Empty,
                IsConnected = true,
                ConnectedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public Task<DriveAccount?> SwitchSessionAsync(string userKey, CancellationToken cancellationToken = default) =>
        TryRestoreSessionAsync(userKey, cancellationToken);

    public async Task DisconnectAsync(string? userKey = null, CancellationToken cancellationToken = default)
    {
        var targetKey = string.IsNullOrWhiteSpace(userKey) ? _activeAccountKey : userKey;

        await _serviceGate.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(targetKey, _activeAccountKey, StringComparison.Ordinal))
            {
                _driveService?.Dispose();
                _driveService = null;
                _activeCredential = null;
            }
        }
        finally
        {
            _serviceGate.Release();
        }

        await _authManager.RevokeAndClearAsync(targetKey, cancellationToken: cancellationToken);
    }

    public async Task<DriveAccount> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var request = service.About.Get();
        request.Fields = "user(displayName,emailAddress,photoLink),storageQuota(limit,usage,usageInDrive,usageInDriveTrash)";

        var about = await request.ExecuteAsync(cancellationToken);
        return new DriveAccount
        {
            Id = _activeAccountKey,
            DisplayName = about.User?.DisplayName ?? "Conta Google",
            EmailAddress = about.User?.EmailAddress ?? string.Empty,
            PhotoUrl = about.User?.PhotoLink,
            IsConnected = true,
            ConnectedAtUtc = DateTimeOffset.UtcNow,
            Quota = new StorageQuotaInfo
            {
                LimitBytes = about.StorageQuota?.Limit ?? 0,
                UsageBytes = about.StorageQuota?.Usage ?? 0,
                UsageInDriveBytes = about.StorageQuota?.UsageInDrive ?? 0,
                UsageInTrashBytes = about.StorageQuota?.UsageInDriveTrash ?? 0
            }
        };
    }

    public async Task<IReadOnlyList<RemoteDriveNode>> ListChildrenAsync(
        string parentFolderId = "root",
        bool foldersOnly = false,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var results = new List<RemoteDriveNode>();
        string? pageToken = null;

        var escapedParent = EscapeQueryValue(string.IsNullOrWhiteSpace(parentFolderId) ? "root" : parentFolderId);
        var query = $"'{escapedParent}' in parents and trashed = false";
        if (foldersOnly)
        {
            query += $" and mimeType = '{MimeTypeHelper.GoogleFolderMimeType}'";
        }

        do
        {
            var request = service.Files.List();
            request.Q = query;
            request.Fields = ListFields;
            request.OrderBy = "folder,name";
            request.PageSize = 200;
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;
            request.PageToken = pageToken;

            var response = await request.ExecuteAsync(cancellationToken);
            if (response.Files is not null)
            {
                foreach (var file in response.Files)
                {
                    results.Add(MapNode(file));
                }
            }

            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    public async Task<IReadOnlyList<RemoteDriveNode>> ListAllRemoteFilesAsync(
        int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var results = new List<RemoteDriveNode>();
        string? pageToken = null;
        var limit = Math.Clamp(maxResults, 1, 1000);

        do
        {
            var request = service.Files.List();
            request.Q = "trashed = false";
            request.Fields = ListFields;
            request.OrderBy = "folder,modifiedTime desc";
            request.PageSize = Math.Min(200, limit - results.Count);
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;
            request.PageToken = pageToken;

            var response = await request.ExecuteAsync(cancellationToken);
            if (response.Files is not null)
            {
                foreach (var file in response.Files)
                {
                    results.Add(MapNode(file));
                    if (results.Count >= limit)
                    {
                        break;
                    }
                }
            }

            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken) && results.Count < limit);

        return results;
    }

    public async Task<IReadOnlyList<RemoteDriveNode>> ListTrashedFilesAsync(
        int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var results = new List<RemoteDriveNode>();
        string? pageToken = null;
        var limit = Math.Clamp(maxResults, 1, 1000);

        do
        {
            var request = service.Files.List();
            request.Q = "trashed = true";
            request.Fields = ListFields;
            request.OrderBy = "folder,modifiedTime desc";
            request.PageSize = Math.Min(200, limit - results.Count);
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;
            request.PageToken = pageToken;

            var response = await request.ExecuteAsync(cancellationToken);
            if (response.Files is not null)
            {
                foreach (var file in response.Files)
                {
                    results.Add(MapNode(file));
                    if (results.Count >= limit)
                    {
                        break;
                    }
                }
            }

            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken) && results.Count < limit);

        return results;
    }

    public async Task<IReadOnlyList<RemoteDriveNode>> SearchRemoteFilesAsync(
        string query,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var escaped = EscapeQueryValue(query.Trim());

        var request = service.Files.List();
        request.Q = $"name contains '{escaped}' and trashed = false";
        request.Fields = ListFields;
        request.OrderBy = "modifiedTime desc";
        request.PageSize = Math.Clamp(maxResults, 1, 100);
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;

        var response = await request.ExecuteAsync(cancellationToken);
        return response.Files?.Select(MapNode).ToList() ?? [];
    }

    public async Task<RemoteDriveNode?> GetFileMetadataAsync(
        string remoteFileId,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var request = service.Files.Get(remoteFileId);
        request.Fields = FileFields;
        request.SupportsAllDrives = true;

        var file = await request.ExecuteAsync(cancellationToken);
        return file is null ? null : MapNode(file);
    }

    public async Task<RemoteDriveNode> CreateFolderAsync(
        string folderName,
        string parentFolderId,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var metadata = new DriveFile
        {
            Name = folderName,
            MimeType = MimeTypeHelper.GoogleFolderMimeType,
            Parents = [string.IsNullOrWhiteSpace(parentFolderId) ? "root" : parentFolderId]
        };

        var request = service.Files.Create(metadata);
        request.Fields = FileFields;
        request.SupportsAllDrives = true;

        var created = await request.ExecuteAsync(cancellationToken);
        return MapNode(created);
    }

    public async Task<RemoteDriveNode> FindOrCreateFolderAsync(
        string folderName,
        string parentFolderId = "root",
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var escapedParent = EscapeQueryValue(string.IsNullOrWhiteSpace(parentFolderId) ? "root" : parentFolderId);
        var escapedName = EscapeQueryValue(folderName.Trim());

        var request = service.Files.List();
        request.Q = $"'{escapedParent}' in parents and name = '{escapedName}' and mimeType = '{MimeTypeHelper.GoogleFolderMimeType}' and trashed = false";
        request.Fields = ListFields;
        request.PageSize = 1;
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;

        var response = await request.ExecuteAsync(cancellationToken);
        var existing = response.Files?.FirstOrDefault();
        if (existing is not null)
        {
            return MapNode(existing);
        }

        return await CreateFolderAsync(folderName.Trim(), parentFolderId, cancellationToken);
    }

    public async Task<RemoteDriveNode> UploadFileAsync(
        string localFilePath,
        string parentFolderId,
        string? existingRemoteFileId = null,
        Action<long>? onBytesTransferred = null,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var fileInfo = new FileInfo(localFilePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Local file for upload was not found.", localFilePath);
        }

        var mimeType = MimeTypeHelper.GetMimeTypeFromFilePath(localFilePath);
        await using var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (!string.IsNullOrWhiteSpace(existingRemoteFileId))
        {
            var updateMeta = new DriveFile
            {
                Name = fileInfo.Name,
                ModifiedTimeDateTimeOffset = fileInfo.LastWriteTimeUtc
            };

            var updateReq = service.Files.Update(updateMeta, existingRemoteFileId, stream, mimeType);
            updateReq.Fields = FileFields;
            updateReq.SupportsAllDrives = true;
            updateReq.ChunkSize = 256 * 1024;
            updateReq.ProgressChanged += progress =>
            {
                if (progress.BytesSent > 0)
                {
                    onBytesTransferred?.Invoke(progress.BytesSent);
                }
            };

            var updateProgress = await updateReq.UploadAsync(cancellationToken);
            if (updateProgress.Status == UploadStatus.Failed)
            {
                throw updateProgress.Exception ?? new IOException($"Upload failed for '{localFilePath}'.");
            }

            onBytesTransferred?.Invoke(fileInfo.Length);
            return MapNode(updateReq.ResponseBody);
        }

        var createMeta = new DriveFile
        {
            Name = fileInfo.Name,
            Parents = [string.IsNullOrWhiteSpace(parentFolderId) ? "root" : parentFolderId],
            ModifiedTimeDateTimeOffset = fileInfo.LastWriteTimeUtc
        };

        var createReq = service.Files.Create(createMeta, stream, mimeType);
        createReq.Fields = FileFields;
        createReq.SupportsAllDrives = true;
        createReq.ChunkSize = 256 * 1024;
        createReq.ProgressChanged += progress =>
        {
            if (progress.BytesSent > 0)
            {
                onBytesTransferred?.Invoke(progress.BytesSent);
            }
        };

        var createProgress = await createReq.UploadAsync(cancellationToken);
        if (createProgress.Status == UploadStatus.Failed)
        {
            throw createProgress.Exception ?? new IOException($"Upload failed for '{localFilePath}'.");
        }

        onBytesTransferred?.Invoke(fileInfo.Length);
        return MapNode(createReq.ResponseBody);
    }

    public async Task DownloadFileAsync(
        string remoteFileId,
        string destinationLocalPath,
        Action<long>? onBytesTransferred = null,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var parentDir = Path.GetDirectoryName(destinationLocalPath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        var tempPath = $"{destinationLocalPath}.tundratmp.{Guid.NewGuid():N}";
        try
        {
            var request = service.Files.Get(remoteFileId);
            request.SupportsAllDrives = true;
            request.MediaDownloader.ChunkSize = 256 * 1024;
            request.MediaDownloader.ProgressChanged += progress =>
            {
                if (progress.BytesDownloaded > 0)
                {
                    onBytesTransferred?.Invoke(progress.BytesDownloaded);
                }
            };

            await using (var outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var result = await request.DownloadAsync(outStream, cancellationToken);
                if (result.Status == DownloadStatus.Failed)
                {
                    throw result.Exception ?? new IOException($"Download failed for remote file '{remoteFileId}'.");
                }
            }

            File.Move(tempPath, destinationLocalPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    public async Task ExportFileAsync(
        string remoteFileId,
        string exportMimeType,
        string destinationLocalPath,
        Action<long>? onBytesTransferred = null,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var parentDir = Path.GetDirectoryName(destinationLocalPath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        var tempPath = $"{destinationLocalPath}.tundratmp.{Guid.NewGuid():N}";
        try
        {
            var request = service.Files.Export(remoteFileId, exportMimeType);
            request.MediaDownloader.ProgressChanged += progress =>
            {
                if (progress.BytesDownloaded > 0)
                {
                    onBytesTransferred?.Invoke(progress.BytesDownloaded);
                }
            };

            await using (var outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var result = await request.DownloadAsync(outStream, cancellationToken);
                if (result.Status == DownloadStatus.Failed)
                {
                    throw result.Exception ?? new IOException($"Export failed for remote file '{remoteFileId}'.");
                }
            }

            File.Move(tempPath, destinationLocalPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    public async Task TrashFileAsync(string remoteFileId, CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var metadata = new DriveFile
        {
            Trashed = true
        };

        var request = service.Files.Update(metadata, remoteFileId);
        request.Fields = "id, trashed";
        request.SupportsAllDrives = true;

        await request.ExecuteAsync(cancellationToken);
        _logger.LogInformation("Moved remote Google Drive file '{RemoteFileId}' to Trash.", remoteFileId);
    }

    public async Task RestoreFileAsync(string remoteFileId, CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var metadata = new DriveFile
        {
            Trashed = false
        };

        var request = service.Files.Update(metadata, remoteFileId);
        request.Fields = "id, trashed";
        request.SupportsAllDrives = true;

        await request.ExecuteAsync(cancellationToken);
        _logger.LogInformation("Restored remote Google Drive file '{RemoteFileId}' from Trash.", remoteFileId);
    }

    public async Task DeletePermanentlyAsync(string remoteFileId, CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var request = service.Files.Delete(remoteFileId);
        request.SupportsAllDrives = true;

        await request.ExecuteAsync(cancellationToken);
        _logger.LogInformation("Permanently deleted remote Google Drive file '{RemoteFileId}'.", remoteFileId);
    }

    public async Task EmptyTrashAsync(CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var request = service.Files.EmptyTrash();

        await request.ExecuteAsync(cancellationToken);
        _logger.LogInformation("Emptied Google Drive Trash.");
    }

    public async Task<RemoteDriveNode> MoveFileAsync(
        string remoteFileId,
        string? destinationParentFolderId = null,
        string? newName = null,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var existing = await GetFileMetadataAsync(remoteFileId, cancellationToken)
            ?? throw new FileNotFoundException($"Remote file '{remoteFileId}' was not found.");

        var metadata = new DriveFile();
        if (!string.IsNullOrWhiteSpace(newName))
        {
            metadata.Name = newName.Trim();
        }

        var request = service.Files.Update(metadata, remoteFileId);
        request.Fields = FileFields;
        request.SupportsAllDrives = true;

        if (!string.IsNullOrWhiteSpace(destinationParentFolderId))
        {
            var targetParent = destinationParentFolderId.Trim();
            var previousParents = string.Join(",", existing.ParentIds.Where(p => !string.IsNullOrWhiteSpace(p) && !string.Equals(p, targetParent, StringComparison.Ordinal)));
            if (!existing.ParentIds.Contains(targetParent, StringComparer.Ordinal))
            {
                request.AddParents = targetParent;
            }

            if (!string.IsNullOrWhiteSpace(previousParents))
            {
                request.RemoveParents = previousParents;
            }
        }

        var updated = await request.ExecuteAsync(cancellationToken);
        _logger.LogInformation("Updated/moved remote Google Drive file '{RemoteFileId}'.", remoteFileId);
        return MapNode(updated);
    }

    public async Task<bool> TrySendGmailAlertAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        string? accountKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return false;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            var credential = string.IsNullOrWhiteSpace(accountKey) ||
                             string.Equals(accountKey, _activeAccountKey, StringComparison.Ordinal)
                ? _activeCredential
                : await _authManager.TryRestoreSilentAsync(accountKey, cts.Token);

            credential ??= !string.IsNullOrWhiteSpace(accountKey)
                ? await _authManager.TryRestoreSilentAsync(accountKey, cts.Token)
                : null;

            if (credential is null)
            {
                return false;
            }

            var accessToken = await credential.GetAccessTokenForRequestAsync(cancellationToken: cts.Token);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return false;
            }

            var raw = BuildGmailRawPayload(recipientEmail, subject, htmlBody);
            var payloadJson = JsonSerializer.Serialize(new { raw });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send")
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var resp = await http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning("Gmail alert skipped with HTTP {StatusCode}: {Body}", (int)resp.StatusCode, errBody);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send Gmail account alert.");
            return false;
        }
    }

    internal static string BuildGmailRawPayload(string recipientEmail, string subject, string htmlBody)
    {
        var cleanEmail = recipientEmail.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();
        var encodedSubject = $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))}?=";
        var bodyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(htmlBody));
        var mimeMessage =
            $"From: Custodia <{cleanEmail}>\r\n" +
            $"To: {cleanEmail}\r\n" +
            $"Subject: {encodedSubject}\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Content-Transfer-Encoding: base64\r\n\r\n" +
            bodyBase64;

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(mimeMessage))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private async Task InitializeDriveServiceAsync(UserCredential credential, string accountKey, CancellationToken cancellationToken)
    {
        await _serviceGate.WaitAsync(cancellationToken);
        try
        {
            _driveService?.Dispose();
            _activeAccountKey = accountKey;
            _activeCredential = credential;
            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Custodia"
            });
        }
        finally
        {
            _serviceGate.Release();
        }
    }

    private DriveService EnsureService() =>
        _driveService ?? throw new InvalidOperationException("Google Drive account is not connected.");

    private static RemoteDriveNode MapNode(DriveFile file) => new()
    {
        Id = file.Id ?? string.Empty,
        Name = file.Name ?? string.Empty,
        MimeType = file.MimeType ?? string.Empty,
        IsFolder = MimeTypeHelper.IsGoogleFolder(file.MimeType),
        IsGoogleWorkspaceDoc = MimeTypeHelper.IsGoogleWorkspaceDocument(file.MimeType),
        SizeBytes = file.Size ?? 0,
        Md5Checksum = file.Md5Checksum,
        ModifiedTimeUtc = file.ModifiedTimeDateTimeOffset ?? DateTimeOffset.UtcNow,
        ParentIds = file.Parents?.ToArray() ?? Array.Empty<string>(),
        Trashed = file.Trashed ?? false
    };

    private static string EscapeQueryValue(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");

    public void Dispose()
    {
        _driveService?.Dispose();
        _serviceGate.Dispose();
    }
}
