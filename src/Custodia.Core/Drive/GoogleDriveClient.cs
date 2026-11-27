using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Json;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.Extensions.Logging;
using TundraDrive.Core.Models;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using Permission = Google.Apis.Drive.v3.Data.Permission;

namespace TundraDrive.Core.Drive;

public sealed class GoogleDriveClient : IGoogleDriveClient, IResumableDriveClient, IDisposable
{
    private const string FileFields = "id, name, mimeType, size, md5Checksum, modifiedTime, parents, trashed, starred, thumbnailLink, webViewLink";
    private const string ListFields = $"nextPageToken, files({FileFields})";
    private const int TransferBufferBytes = 64 * 1024;
    internal const int MinTransferChunkSizeBytes = 256 * 1024;
    internal const int DownloadChunkSizeBytes = 4 * 1024 * 1024;

    internal static int ResolveUploadChunkSize(long fileLengthBytes) => fileLengthBytes switch
    {
        >= 64L * 1024 * 1024 => 8 * 1024 * 1024,
        >= 4L * 1024 * 1024 => 4 * 1024 * 1024,
        _ => 1 * 1024 * 1024
    };

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

    public Task<IReadOnlyList<RemoteDriveNode>> ListAllRemoteFilesAsync(
        int maxResults = 200,
        CancellationToken cancellationToken = default) =>
        ListFilesByQueryAsync("trashed = false", maxResults, cancellationToken);

    public Task<IReadOnlyList<RemoteDriveNode>> ListStarredFilesAsync(
        int maxResults = 200,
        CancellationToken cancellationToken = default) =>
        ListFilesByQueryAsync("starred = true and trashed = false", maxResults, cancellationToken);

    public Task<IReadOnlyList<RemoteDriveNode>> ListTrashedFilesAsync(
        int maxResults = 200,
        CancellationToken cancellationToken = default) =>
        ListFilesByQueryAsync("trashed = true", maxResults, cancellationToken);

    public Task<IReadOnlyList<RemoteDriveNode>> ListSharedWithMeAsync(
        int maxResults = 200,
        CancellationToken cancellationToken = default) =>
        ListFilesByQueryAsync("sharedWithMe = true and trashed = false", maxResults, cancellationToken);

    public async Task<byte[]?> DownloadThumbnailAsync(
        string remoteFileId,
        string? thumbnailLink,
        CancellationToken cancellationToken = default)
    {
        var service = _driveService;
        if (service is null || string.IsNullOrWhiteSpace(thumbnailLink))
        {
            return null;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await service.HttpClient.GetAsync(thumbnailLink, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Thumbnail fetch failed for remote file '{RemoteFileId}'.", remoteFileId);
            return null;
        }
    }

    public async Task<string?> GetShareLinkAsync(
        string remoteFileId,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var request = service.Files.Get(remoteFileId);
        request.Fields = "id, webViewLink";
        request.SupportsAllDrives = true;

        var file = await request.ExecuteAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(file?.WebViewLink))
        {
            return file.WebViewLink;
        }

        return $"https://drive.google.com/file/d/{Uri.EscapeDataString(remoteFileId)}/view";
    }

    public async Task<IReadOnlyList<DrivePermissionEntry>> ListPermissionsAsync(
        string remoteFileId,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var request = service.Permissions.List(remoteFileId);
        request.Fields = "permissions(id, type, role, emailAddress, displayName)";
        request.SupportsAllDrives = true;

        var response = await request.ExecuteAsync(cancellationToken);
        return response.Permissions?.Select(MapPermission).ToList() ?? [];
    }

    public async Task<DrivePermissionEntry> ShareAnyoneAsync(
        string remoteFileId,
        string role,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var normalizedRole = string.Equals(role, "writer", StringComparison.OrdinalIgnoreCase) ? "writer" : "reader";

        var existing = await ListPermissionsAsync(remoteFileId, cancellationToken);
        var anyonePerm = existing.FirstOrDefault(p => string.Equals(p.Type, "anyone", StringComparison.OrdinalIgnoreCase));
        if (anyonePerm is not null)
        {
            if (string.Equals(anyonePerm.Role, normalizedRole, StringComparison.OrdinalIgnoreCase))
            {
                return anyonePerm;
            }

            var updateReq = service.Permissions.Update(new Permission { Role = normalizedRole }, remoteFileId, anyonePerm.Id);
            updateReq.Fields = "id, type, role, emailAddress, displayName";
            updateReq.SupportsAllDrives = true;
            var updated = await updateReq.ExecuteAsync(cancellationToken);
            return MapPermission(updated);
        }

        var createReq = service.Permissions.Create(
            new Permission { Type = "anyone", Role = normalizedRole },
            remoteFileId);
        createReq.Fields = "id, type, role, emailAddress, displayName";
        createReq.SupportsAllDrives = true;
        var created = await createReq.ExecuteAsync(cancellationToken);
        return MapPermission(created);
    }

    public async Task<DrivePermissionEntry> InviteUserPermissionAsync(
        string remoteFileId,
        string emailAddress,
        string role,
        bool sendNotificationEmail = true,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var normalizedRole = string.Equals(role, "writer", StringComparison.OrdinalIgnoreCase) ? "writer" : "reader";
        var request = service.Permissions.Create(
            new Permission
            {
                Type = "user",
                Role = normalizedRole,
                EmailAddress = emailAddress.Trim()
            },
            remoteFileId);
        request.SendNotificationEmail = sendNotificationEmail;
        request.Fields = "id, type, role, emailAddress, displayName";
        request.SupportsAllDrives = true;

        var created = await request.ExecuteAsync(cancellationToken);
        return MapPermission(created);
    }

    public async Task RevokePermissionAsync(
        string remoteFileId,
        string permissionId,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var request = service.Permissions.Delete(remoteFileId, permissionId);
        request.SupportsAllDrives = true;
        await request.ExecuteAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<RemoteDriveNode>> ListFilesByQueryAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var service = EnsureService();
        var results = new List<RemoteDriveNode>();
        string? pageToken = null;
        var limit = Math.Clamp(maxResults, 1, 1000);

        do
        {
            var request = service.Files.List();
            request.Q = query;
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
        var chunkSize = ResolveUploadChunkSize(fileInfo.Length);
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
            updateReq.ChunkSize = chunkSize;
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

            if (updateReq.ResponseBody is null)
            {
                throw new IOException($"Upload did not complete for '{localFilePath}' (status: {updateProgress.Status}).");
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
        createReq.ChunkSize = chunkSize;
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

        if (createReq.ResponseBody is null)
        {
            throw new IOException($"Upload did not complete for '{localFilePath}' (status: {createProgress.Status}).");
        }

        onBytesTransferred?.Invoke(fileInfo.Length);
        return MapNode(createReq.ResponseBody);
    }

    public async Task<RemoteDriveNode> ResumableUploadAsync(
        string localFilePath,
        string parentFolderId,
        string? existingRemoteFileId = null,
        string? resumeSessionUri = null,
        Func<string, Task>? onSessionCreated = null,
        Action<long>? onBytesTransferred = null,
        Func<long>? getBandwidthLimitBytesPerSecond = null,
        TransferBandwidthLimiter? bandwidthLimiter = null,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var fileInfo = new FileInfo(localFilePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Local file for upload was not found.", localFilePath);
        }

        var mimeType = MimeTypeHelper.GetMimeTypeFromFilePath(localFilePath);
        var totalBytes = fileInfo.Length;
        var chunkSize = ResolveUploadChunkSize(totalBytes);
        var sessionUri = resumeSessionUri;
        long offset = 0;

        if (!string.IsNullOrWhiteSpace(sessionUri))
        {
            var probe = await ProbeResumableUploadOffsetAsync(service.HttpClient, sessionUri, totalBytes, cancellationToken);
            if (probe.CompletedFile is not null)
            {
                onBytesTransferred?.Invoke(totalBytes);
                return MapNode(probe.CompletedFile);
            }

            if (probe.SessionExpired)
            {
                sessionUri = null;
            }
            else
            {
                offset = Math.Clamp(probe.NextOffset, 0, totalBytes);
                if (offset > 0)
                {
                    onBytesTransferred?.Invoke(offset);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(sessionUri))
        {
            sessionUri = await InitiateResumableUploadSessionAsync(
                service.HttpClient,
                fileInfo,
                parentFolderId,
                existingRemoteFileId,
                mimeType,
                cancellationToken);
            offset = 0;
            if (onSessionCreated is not null)
            {
                await onSessionCreated(sessionUri);
            }
        }

        await using var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (totalBytes == 0)
        {
            using var emptyRequest = new HttpRequestMessage(HttpMethod.Put, sessionUri)
            {
                Content = new ByteArrayContent([])
            };
            emptyRequest.Content.Headers.ContentLength = 0;
            emptyRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            using var emptyResponse = await service.HttpClient.SendAsync(emptyRequest, cancellationToken);
            var body = await emptyResponse.Content.ReadAsStringAsync(cancellationToken);
            emptyResponse.EnsureSuccessStatusCode();
            var completed = DeserializeDriveFile(body)
                ?? throw new IOException($"Upload did not return file metadata for '{localFilePath}'.");
            onBytesTransferred?.Invoke(0);
            return MapNode(completed);
        }

        while (offset < totalBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkLength = (int)Math.Min(chunkSize, totalBytes - offset);
            var chunkContent = new ThrottledFileSliceContent(
                fileStream,
                offset,
                chunkLength,
                mimeType,
                bytesSent => onBytesTransferred?.Invoke(bytesSent),
                bandwidthLimiter,
                getBandwidthLimitBytesPerSecond);
            chunkContent.Headers.TryAddWithoutValidation("Content-Range", $"bytes {offset}-{offset + chunkLength - 1}/{totalBytes}");

            using var chunkRequest = new HttpRequestMessage(HttpMethod.Put, sessionUri)
            {
                Content = chunkContent
            };

            using var chunkResponse = await service.HttpClient.SendAsync(
                chunkRequest,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            var statusCode = (int)chunkResponse.StatusCode;
            if (statusCode == 308)
            {
                offset = ParseRangeEnd(chunkResponse.Headers, offset + chunkLength);
                onBytesTransferred?.Invoke(offset);
                continue;
            }

            if (statusCode is 404 or 410)
            {
                // Restart once cleanly if the remote upload session expired mid-transfer.
                sessionUri = await InitiateResumableUploadSessionAsync(
                    service.HttpClient,
                    fileInfo,
                    parentFolderId,
                    existingRemoteFileId,
                    mimeType,
                    cancellationToken);
                offset = 0;
                if (onSessionCreated is not null)
                {
                    await onSessionCreated(sessionUri);
                }
                continue;
            }

            var responseBody = await chunkResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!chunkResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Resumable upload failed with status {(int)chunkResponse.StatusCode}: {responseBody}",
                    null,
                    chunkResponse.StatusCode);
            }

            var uploadedFile = DeserializeDriveFile(responseBody)
                ?? throw new IOException($"Upload completed without metadata for '{localFilePath}'.");
            onBytesTransferred?.Invoke(totalBytes);
            return MapNode(uploadedFile);
        }

        throw new IOException($"Resumable upload ended unexpectedly for '{localFilePath}'.");
    }

    public async Task ResumableDownloadAsync(
        string remoteFileId,
        string destinationLocalPath,
        string? resumeTempPath = null,
        long knownTotalBytes = 0,
        Func<string, Task>? onTempPathAssigned = null,
        Action<long>? onBytesTransferred = null,
        Func<long>? getBandwidthLimitBytesPerSecond = null,
        TransferBandwidthLimiter? bandwidthLimiter = null,
        bool keepTempOnInterrupt = true,
        CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var parentDir = Path.GetDirectoryName(destinationLocalPath);
        if (!string.IsNullOrWhiteSpace(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        var tempPath = string.IsNullOrWhiteSpace(resumeTempPath)
            ? BuildDeterministicTempPath(remoteFileId, destinationLocalPath)
            : resumeTempPath;

        if (onTempPathAssigned is not null)
        {
            await onTempPathAssigned(tempPath);
        }

        var retriedAfterRangeReset = false;
        while (true)
        {
            long existingOffset = 0;
            if (File.Exists(tempPath))
            {
                existingOffset = new FileInfo(tempPath).Length;
                if (knownTotalBytes > 0 && existingOffset > knownTotalBytes)
                {
                    File.Delete(tempPath);
                    existingOffset = 0;
                }
            }

            if (existingOffset > 0)
            {
                onBytesTransferred?.Invoke(existingOffset);
            }

            var mediaUrl = $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(remoteFileId)}?alt=media&supportsAllDrives=true";
            using var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
            if (existingOffset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingOffset, null);
            }

            try
            {
                using var response = await service.HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existingOffset > 0 && !retriedAfterRangeReset)
                {
                    retriedAfterRangeReset = true;
                    File.Delete(tempPath);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var append = existingOffset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (!append)
                {
                    existingOffset = 0;
                }

                await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var outStream = new FileStream(
                    tempPath,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    var buffer = new byte[TransferBufferBytes];
                    long totalWritten = existingOffset;
                    while (true)
                    {
                        var read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                        if (read <= 0)
                        {
                            break;
                        }

                        var limitBps = getBandwidthLimitBytesPerSecond?.Invoke() ?? 0L;
                        if (bandwidthLimiter is not null && limitBps > 0)
                        {
                            await bandwidthLimiter.ThrottleAsync(read, limitBps, cancellationToken);
                        }

                        await outStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        totalWritten += read;
                        onBytesTransferred?.Invoke(totalWritten);
                    }
                }

                File.Move(tempPath, destinationLocalPath, overwrite: true);
                return;
            }
            catch
            {
                if (!keepTempOnInterrupt && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }

                throw;
            }
        }
    }

    private static string BuildDeterministicTempPath(string remoteFileId, string destinationLocalPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{remoteFileId}|{destinationLocalPath}")))[..12].ToLowerInvariant();
        return $"{destinationLocalPath}.tundratmp.{hash}";
    }

    private static async Task<string> InitiateResumableUploadSessionAsync(
        HttpClient httpClient,
        FileInfo fileInfo,
        string parentFolderId,
        string? existingRemoteFileId,
        string mimeType,
        CancellationToken cancellationToken)
    {
        var isUpdate = !string.IsNullOrWhiteSpace(existingRemoteFileId);
        var url = isUpdate
            ? $"https://www.googleapis.com/upload/drive/v3/files/{Uri.EscapeDataString(existingRemoteFileId!)}?uploadType=resumable&supportsAllDrives=true&fields={Uri.EscapeDataString(FileFields)}"
            : $"https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&supportsAllDrives=true&fields={Uri.EscapeDataString(FileFields)}";

        object payload = isUpdate
            ? new
            {
                name = fileInfo.Name,
                modifiedTime = fileInfo.LastWriteTimeUtc.ToString("O")
            }
            : new
            {
                name = fileInfo.Name,
                parents = new[] { string.IsNullOrWhiteSpace(parentFolderId) ? "root" : parentFolderId },
                modifiedTime = fileInfo.LastWriteTimeUtc.ToString("O")
            };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(isUpdate ? HttpMethod.Patch : HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-Upload-Content-Type", mimeType);
        request.Headers.TryAddWithoutValidation("X-Upload-Content-Length", fileInfo.Length.ToString());

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Failed to start resumable upload session ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        var sessionUri = response.Headers.Location?.ToString();
        if (string.IsNullOrWhiteSpace(sessionUri))
        {
            throw new IOException("Google Drive did not return a resumable upload session URI.");
        }

        return sessionUri;
    }

    private static async Task<(long NextOffset, bool SessionExpired, DriveFile? CompletedFile)> ProbeResumableUploadOffsetAsync(
        HttpClient httpClient,
        string sessionUri,
        long totalBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, sessionUri)
        {
            Content = new ByteArrayContent([])
        };
        request.Content.Headers.ContentLength = 0;
        request.Content.Headers.TryAddWithoutValidation("Content-Range", $"bytes */{totalBytes}");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var code = (int)response.StatusCode;
        if (code == 308)
        {
            return (ParseRangeEnd(response.Headers, 0), false, null);
        }

        if (code is 200 or 201)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (totalBytes, false, DeserializeDriveFile(body));
        }

        return (0, true, null);
    }

    private static long ParseRangeEnd(HttpResponseHeaders headers, long fallbackOffset)
    {
        if (!headers.TryGetValues("Range", out var values))
        {
            return fallbackOffset;
        }

        var raw = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallbackOffset;
        }

        var dashIdx = raw.LastIndexOf('-');
        if (dashIdx >= 0 && long.TryParse(raw[(dashIdx + 1)..], out var lastByte) && lastByte >= 0)
        {
            return lastByte + 1;
        }

        return fallbackOffset;
    }

    private static DriveFile? DeserializeDriveFile(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return NewtonsoftJsonSerializer.Instance.Deserialize<DriveFile>(json);
    }

    private sealed class ThrottledFileSliceContent : HttpContent
    {
        private readonly Stream _sourceStream;
        private readonly long _startOffset;
        private readonly long _length;
        private readonly Action<long>? _onBytesSent;
        private readonly TransferBandwidthLimiter? _limiter;
        private readonly Func<long>? _getBytesPerSecond;

        public ThrottledFileSliceContent(
            Stream sourceStream,
            long startOffset,
            long length,
            string contentType,
            Action<long>? onBytesSent,
            TransferBandwidthLimiter? limiter,
            Func<long>? getBytesPerSecond)
        {
            _sourceStream = sourceStream;
            _startOffset = startOffset;
            _length = length;
            _onBytesSent = onBytesSent;
            _limiter = limiter;
            _getBytesPerSecond = getBytesPerSecond;
            Headers.ContentType = new MediaTypeHeaderValue(contentType);
            Headers.ContentLength = length;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            _sourceStream.Seek(_startOffset, SeekOrigin.Begin);
            var buffer = new byte[TransferBufferBytes];
            long remaining = _length;
            long sent = 0;

            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = await _sourceStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                var limitBps = _getBytesPerSecond?.Invoke() ?? 0L;
                if (_limiter is not null && limitBps > 0)
                {
                    await _limiter.ThrottleAsync(read, limitBps, cancellationToken);
                }

                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
                sent += read;
                _onBytesSent?.Invoke(_startOffset + sent);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }
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
            request.MediaDownloader.ChunkSize = DownloadChunkSizeBytes;
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

    public async Task SetStarredAsync(string remoteFileId, bool starred, CancellationToken cancellationToken = default)
    {
        var service = EnsureService();
        var metadata = new DriveFile
        {
            Starred = starred
        };

        var request = service.Files.Update(metadata, remoteFileId);
        request.Fields = "id, starred";
        request.SupportsAllDrives = true;

        await request.ExecuteAsync(cancellationToken);
        _logger.LogInformation("Updated Google Drive file '{RemoteFileId}' starred={Starred}.", remoteFileId, starred);
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
        ThumbnailLink = file.ThumbnailLink,
        WebViewLink = file.WebViewLink,
        ModifiedTimeUtc = file.ModifiedTimeDateTimeOffset ?? DateTimeOffset.UtcNow,
        ParentIds = file.Parents?.ToArray() ?? Array.Empty<string>(),
        Trashed = file.Trashed ?? false,
        Starred = file.Starred ?? false
    };

    private static DrivePermissionEntry MapPermission(Permission permission) => new()
    {
        Id = permission.Id ?? string.Empty,
        Type = permission.Type ?? string.Empty,
        Role = permission.Role ?? string.Empty,
        EmailAddress = permission.EmailAddress,
        DisplayName = permission.DisplayName
    };

    private static string EscapeQueryValue(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");

    public void Dispose()
    {
        _driveService?.Dispose();
        _serviceGate.Dispose();
    }
}
