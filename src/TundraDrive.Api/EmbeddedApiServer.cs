using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TundraDrive.Api.Contracts;
using TundraDrive.Core.Configuration;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Models;
using TundraDrive.Core.Persistence;
using TundraDrive.Core.Sync;

namespace TundraDrive.Api;

public sealed class EmbeddedApiServer : IEmbeddedApiServer
{
    private readonly IDriveSyncService _syncService;
    private readonly IGoogleDriveClient _driveClient;
    private readonly ISyncStateRepository _repository;
    private readonly IAppSettingsService _settingsService;
    private readonly ILogger<EmbeddedApiServer> _logger;

    private WebApplication? _app;
    private int _boundPort = 5080;

    public EmbeddedApiServer(
        IDriveSyncService syncService,
        IGoogleDriveClient driveClient,
        ISyncStateRepository repository,
        IAppSettingsService settingsService,
        ILogger<EmbeddedApiServer> logger)
    {
        _syncService = syncService;
        _driveClient = driveClient;
        _repository = repository;
        _settingsService = settingsService;
        _logger = logger;
    }

    public bool IsRunning { get; private set; }
    public string BoundAddress => $"http://127.0.0.1:{_boundPort}";
    public string? LastError { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        if (!_settingsService.Current.EmbeddedApiEnabled)
        {
            _logger.LogInformation("Embedded local API is disabled in settings.");
            return;
        }

        _boundPort = _settingsService.Current.EmbeddedApiPort > 0
            ? _settingsService.Current.EmbeddedApiPort
            : 5080;

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            // Loopback only (127.0.0.1)
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, _boundPort);
            });

            var app = builder.Build();

            // Protect against DNS rebinding and cross-site browser CSRF while keeping local scripts/agents working
            app.Use(async (context, next) =>
            {
                var hostHeader = context.Request.Headers.Host.FirstOrDefault();
                var originHeader = context.Request.Headers.Origin.FirstOrDefault();
                var refererHeader = context.Request.Headers.Referer.FirstOrDefault();
                var secFetchSite = context.Request.Headers["Sec-Fetch-Site"].FirstOrDefault();
                if (!IsAllowedLoopbackRequest(hostHeader, originHeader, refererHeader, secFetchSite))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Forbidden: only local loopback requests are permitted."
                    });
                    return;
                }

                await next();
            });

            MapEndpoints(app);

            await app.StartAsync(cancellationToken);
            _app = app;
            IsRunning = true;
            LastError = null;
            _logger.LogInformation("Embedded local REST API listening on {Address}.", BoundAddress);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            IsRunning = false;
            _logger.LogError(ex, "Failed to start embedded REST API on {Address}.", BoundAddress);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app is null)
        {
            return;
        }

        try
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
        }
        finally
        {
            _app = null;
            IsRunning = false;
        }
    }

    private void MapEndpoints(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/status", async (CancellationToken ct) =>
        {
            var folders = await _syncService.GetSyncFoldersAsync(ct);
            var transfers = _syncService.GetTransferQueueSnapshot();
            var activeCount = transfers.Count(t =>
                t.Status is TransferStatus.Queued or TransferStatus.InProgress);

            var account = _syncService.CurrentAccount;
            var folderDtos = folders.Select(f => new SyncFolderSummaryDto(
                f.Id,
                f.LocalPath,
                f.RemoteFolderId,
                f.RemoteFolderName,
                f.Mode.ToString(),
                f.State.ToString(),
                f.IsPaused,
                f.LastSyncedAtUtc,
                f.TrackedFileCount)).ToList();

            return Results.Ok(new ApiStatusResponse(
                ServiceName: "TundraDrive",
                BoundEndpoint: BoundAddress,
                IsAccountConnected: account.IsConnected,
                AccountEmail: account.IsConnected ? account.EmailAddress : null,
                IsSyncRunning: _syncService.IsSyncRunning,
                StorageUsageBytes: account.Quota.UsageBytes,
                StorageLimitBytes: account.Quota.LimitBytes,
                ActiveTransferCount: activeCount,
                SyncFolders: folderDtos));
        });

        api.MapPost("/sync/trigger", async (TriggerSyncRequest? request, CancellationToken ct) =>
        {
            if (!_driveClient.IsConnected)
            {
                return Results.Problem(
                    title: "Google Drive account not connected",
                    detail: "Connect a Google account in TundraDrive before triggering synchronization.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var folderId = request?.FolderId;
            var wait = request?.WaitForCompletion ?? false;

            if (!string.IsNullOrWhiteSpace(folderId))
            {
                var folder = await _repository.GetSyncFolderByIdAsync(folderId, ct);
                if (folder is null)
                {
                    return Results.NotFound(new { error = $"Sync folder '{folderId}' not found." });
                }

                if (wait)
                {
                    await _syncService.TriggerFolderSyncAsync(folderId, ct);
                }
                else
                {
                    _ = Task.Run(() => _syncService.TriggerFolderSyncAsync(folderId), CancellationToken.None);
                }

                return Results.Ok(new TriggerSyncResponse(
                    Accepted: true,
                    Scope: folderId,
                    Message: $"Sync triggered for folder '{folder.LocalPath}'."));
            }

            if (wait)
            {
                await _syncService.TriggerSyncAllAsync(ct);
            }
            else
            {
                _ = Task.Run(() => _syncService.TriggerSyncAllAsync(), CancellationToken.None);
            }

            return Results.Ok(new TriggerSyncResponse(
                Accepted: true,
                Scope: "all",
                Message: "Full synchronization triggered for all active folders."));
        });

        api.MapGet("/files/search", async (
            string? q,
            string? folderId,
            bool? includeRemote,
            int? limit,
            CancellationToken ct) =>
        {
            var query = q?.Trim() ?? string.Empty;
            var max = Math.Clamp(limit ?? 50, 1, 100);
            var items = new List<FileSearchItemDto>();

            var trackedMatches = await _repository.SearchTrackedFilesAsync(query, folderId, max, ct);
            items.AddRange(trackedMatches.Select(t => new FileSearchItemDto(
                Source: "LocalTracked",
                SyncFolderId: t.SyncFolderId,
                Name: Path.GetFileName(t.RelativePath),
                RelativeOrRemotePath: t.RelativePath,
                RemoteFileId: t.RemoteFileId,
                SizeBytes: t.SizeBytes,
                Md5Hash: t.Md5Hash,
                ModifiedAtUtc: t.LocalModifiedAtUtc)));

            if ((includeRemote ?? false) && _driveClient.IsConnected && !string.IsNullOrWhiteSpace(query))
            {
                var existingRemoteIds = items
                    .Select(i => i.RemoteFileId)
                    .ToHashSet(StringComparer.Ordinal);

                var remoteMatches = await _driveClient.SearchRemoteFilesAsync(query, max, ct);
                foreach (var node in remoteMatches)
                {
                    if (existingRemoteIds.Contains(node.Id))
                    {
                        continue;
                    }

                    items.Add(new FileSearchItemDto(
                        Source: "GoogleDriveRemote",
                        SyncFolderId: null,
                        Name: node.Name,
                        RelativeOrRemotePath: node.Name,
                        RemoteFileId: node.Id,
                        SizeBytes: node.SizeBytes,
                        Md5Hash: node.Md5Checksum,
                        ModifiedAtUtc: node.ModifiedTimeUtc));
                }
            }

            return Results.Ok(new FileSearchResponse(query, items.Count, items));
        });

        api.MapPost("/files/upload", async (UploadFileRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.LocalPath))
            {
                return Results.BadRequest(new { error = "localPath is required." });
            }

            if (!_driveClient.IsConnected)
            {
                return Results.Problem(
                    title: "Google Drive account not connected",
                    detail: "Connect a Google account in TundraDrive before uploading files.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!File.Exists(request.LocalPath))
            {
                return Results.NotFound(new { error = $"File not found: '{request.LocalPath}'." });
            }

            var fullUploadPath = Path.GetFullPath(request.LocalPath.Trim());
            if (!IsSafeLocalApiFilePath(fullUploadPath, out var uploadBlockReason))
            {
                return Results.BadRequest(new { error = uploadBlockReason });
            }

            var remoteParentId = string.IsNullOrWhiteSpace(request.RemoteFolderId)
                ? "root"
                : request.RemoteFolderId;

            var uploaded = await _syncService.UploadSingleFileAsync(fullUploadPath, remoteParentId, ct);
            return Results.Ok(new UploadFileResponse(
                Success: true,
                RemoteFileId: uploaded.Id,
                FileName: uploaded.Name,
                SizeBytes: uploaded.SizeBytes,
                Md5Checksum: uploaded.Md5Checksum));
        });

        api.MapGet("/files/list", async (string? folderId, bool? foldersOnly, CancellationToken ct) =>
        {
            if (!_driveClient.IsConnected)
            {
                return NotConnectedProblem();
            }

            var targetFolder = string.IsNullOrWhiteSpace(folderId) ? "root" : folderId.Trim();
            var nodes = await _driveClient.ListChildrenAsync(targetFolder, foldersOnly ?? false, ct);
            var dtos = nodes.Select(MapNodeDto).ToList();
            return Results.Ok(new FileListResponse(targetFolder, dtos.Count, dtos));
        });

        api.MapGet("/files/trash", async (int? limit, CancellationToken ct) =>
        {
            if (!_driveClient.IsConnected)
            {
                return NotConnectedProblem();
            }

            var max = Math.Clamp(limit ?? 50, 1, 200);
            var nodes = await _driveClient.ListTrashedFilesAsync(max, ct);
            var dtos = nodes.Select(MapNodeDto).ToList();
            return Results.Ok(new FileListResponse("trash", dtos.Count, dtos));
        });

        api.MapPost("/folders", async (CreateFolderRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "name is required." });
            }

            if (!_driveClient.IsConnected)
            {
                return NotConnectedProblem();
            }

            var parentId = string.IsNullOrWhiteSpace(request.ParentFolderId) ? "root" : request.ParentFolderId.Trim();
            var created = await _driveClient.CreateFolderAsync(request.Name.Trim(), parentId, ct);
            return Results.Ok(MapNodeDto(created));
        });

        api.MapPost("/files/download", async (DownloadFileRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.RemoteFileId))
            {
                return Results.BadRequest(new { error = "remoteFileId is required." });
            }

            if (!_driveClient.IsConnected)
            {
                return NotConnectedProblem();
            }

            RemoteDriveNode? meta;
            try
            {
                meta = await _driveClient.GetFileMetadataAsync(request.RemoteFileId.Trim(), ct);
            }
            catch
            {
                meta = null;
            }

            if (meta is null)
            {
                return Results.NotFound(new { error = $"Remote file '{request.RemoteFileId}' not found." });
            }

            if (meta.IsFolder)
            {
                return Results.BadRequest(new { error = "Cannot download a folder directly." });
            }

            string savedPath;
            if (!string.IsNullOrWhiteSpace(request.DestinationPath))
            {
                savedPath = Path.GetFullPath(request.DestinationPath.Trim());
                if (Directory.Exists(savedPath))
                {
                    var safeName = DriveSyncService.SanitizeRemoteSegment(meta.Name);
                    savedPath = DriveSyncService.ResolveSafeLocalPath(savedPath, safeName);
                }

                if (!IsSafeLocalApiFilePath(savedPath, out var downloadBlockReason))
                {
                    return Results.BadRequest(new { error = downloadBlockReason });
                }

                if (meta.IsGoogleWorkspaceDoc)
                {
                    await _driveClient.ExportFileAsync(meta.Id, "application/pdf", savedPath, null, ct);
                }
                else
                {
                    await _driveClient.DownloadFileAsync(meta.Id, savedPath, null, ct);
                }
            }
            else
            {
                savedPath = await _syncService.DownloadRemoteFileAsync(
                    meta.Id,
                    meta.Name,
                    meta.SizeBytes,
                    meta.IsGoogleWorkspaceDoc,
                    meta.MimeType,
                    ct);
            }

            var size = File.Exists(savedPath) ? new FileInfo(savedPath).Length : meta.SizeBytes;
            return Results.Ok(new DownloadFileResponse(
                Success: true,
                RemoteFileId: meta.Id,
                FileName: Path.GetFileName(savedPath),
                SavedPath: savedPath,
                SizeBytes: size));
        });

        api.MapPost("/files/{remoteFileId}/move", async (string remoteFileId, MoveFileRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(remoteFileId))
            {
                return Results.BadRequest(new { error = "remoteFileId is required." });
            }

            if (string.IsNullOrWhiteSpace(request.TargetFolderId) && string.IsNullOrWhiteSpace(request.NewName))
            {
                return Results.BadRequest(new { error = "Provide targetFolderId and/or newName." });
            }

            if (!_driveClient.IsConnected)
            {
                return NotConnectedProblem();
            }

            try
            {
                var moved = await _driveClient.MoveFileAsync(remoteFileId.Trim(), request.TargetFolderId, request.NewName, ct);
                return Results.Ok(new MoveFileResponse(
                    Success: true,
                    RemoteFileId: moved.Id,
                    FileName: moved.Name,
                    ParentIds: moved.ParentIds));
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        api.MapPost("/files/{remoteFileId}/trash", async (string remoteFileId, CancellationToken ct) =>
        {
            if (!_driveClient.IsConnected)
            {
                return NotConnectedProblem();
            }

            var existing = await _driveClient.GetFileMetadataAsync(remoteFileId.Trim(), ct);
            if (existing is null)
            {
                return Results.NotFound(new { error = $"Remote file '{remoteFileId}' not found." });
            }

            await _syncService.TrashRemoteItemAsync(existing.Id, existing.Name, ct);
            return Results.Ok(new FileActionResponse(true, remoteFileId.Trim(), "trashed"));
        });

        api.MapPost("/files/{remoteFileId}/restore", async (string remoteFileId, CancellationToken ct) =>
        {
            if (!_driveClient.IsConnected)
            {
                return NotConnectedProblem();
            }

            var existing = await _driveClient.GetFileMetadataAsync(remoteFileId.Trim(), ct);
            if (existing is null)
            {
                return Results.NotFound(new { error = $"Remote file '{remoteFileId}' not found." });
            }

            await _syncService.RestoreRemoteItemAsync(existing.Id, existing.Name, ct);
            return Results.Ok(new FileActionResponse(true, remoteFileId.Trim(), "restored"));
        });

        api.MapDelete("/files/{remoteFileId}", async (string remoteFileId, bool? permanent, CancellationToken ct) =>
        {
            if (!_driveClient.IsConnected)
            {
                return NotConnectedProblem();
            }

            var id = remoteFileId.Trim();
            var existing = await _driveClient.GetFileMetadataAsync(id, ct);
            var itemName = existing?.Name ?? id;
            if (permanent ?? false)
            {
                await _syncService.DeleteRemoteItemPermanentlyAsync(id, itemName, ct);
                return Results.Ok(new FileActionResponse(true, id, "deleted_permanently"));
            }

            await _syncService.TrashRemoteItemAsync(id, itemName, ct);
            return Results.Ok(new FileActionResponse(true, id, "trashed"));
        });

        api.MapGet("/transfers", () =>
        {
            var snapshot = _syncService.GetTransferQueueSnapshot();
            var dtos = snapshot.Select(t => new TransferItemDto(
                t.Id,
                t.SyncFolderId,
                t.FileName,
                t.RelativePath,
                t.Direction.ToString(),
                t.Status.ToString(),
                t.TotalBytes,
                t.TransferredBytes,
                t.ErrorMessage,
                t.CreatedAtUtc,
                t.CompletedAtUtc)).ToList();
            var activeCount = snapshot.Count(t => t.Status is TransferStatus.Queued or TransferStatus.InProgress);
            return Results.Ok(new TransfersResponse(dtos.Count, activeCount, dtos));
        });

        api.MapGet("/logs", async (string? q, int? limit, CancellationToken ct) =>
        {
            var max = Math.Clamp(limit ?? 50, 1, 250);
            var logs = await _repository.GetActivityLogsAsync(null, q, max, ct);
            var dtos = logs.Select(l => new ActivityLogDto(
                l.Id,
                l.SyncFolderId,
                l.Severity.ToString(),
                l.Action,
                l.Message,
                l.TargetPath,
                l.CreatedAtUtc)).ToList();
            return Results.Ok(new ActivityLogsResponse(dtos.Count, dtos));
        });

        api.MapGet("/openapi.json", () => Results.Text(BuildOpenApiJson(BoundAddress), "application/json"));
    }

    private static string BuildOpenApiJson(string serverUrl)
    {
        var doc = new
        {
            openapi = "3.0.3",
            info = new
            {
                title = "TundraDrive Local REST API",
                version = TundraDrive.Core.Configuration.AppVersionInfo.CurrentVersion,
                description = "API REST local (loopback 127.0.0.1) do TundraDrive para automacao, scripts e agentes de IA."
            },
            servers = new[] { new { url = serverUrl } },
            paths = new Dictionary<string, object>
            {
                ["/api/v1/status"] = new { get = new { summary = "Estado geral, conta, cota de armazenamento e pastas sincronizadas", operationId = "getStatus" } },
                ["/api/v1/sync/trigger"] = new { post = new { summary = "Dispara sincronizacao completa ou de uma pasta (body opcional: folderId, waitForCompletion)", operationId = "triggerSync" } },
                ["/api/v1/files/search"] = new { get = new { summary = "Busca arquivos rastreados localmente e opcionalmente no Drive (q, folderId, includeRemote, limit)", operationId = "searchFiles" } },
                ["/api/v1/files/list"] = new { get = new { summary = "Lista arquivos e pastas remotas de um folderId do Google Drive", operationId = "listFiles" } },
                ["/api/v1/files/trash"] = new { get = new { summary = "Lista itens na Lixeira do Google Drive", operationId = "listTrash" } },
                ["/api/v1/folders"] = new { post = new { summary = "Cria pasta remota no Google Drive (name, parentFolderId)", operationId = "createFolder" } },
                ["/api/v1/files/upload"] = new { post = new { summary = "Envia um arquivo local para o Google Drive (localPath, remoteFolderId)", operationId = "uploadFile" } },
                ["/api/v1/files/download"] = new { post = new { summary = "Baixa arquivo do Google Drive (remoteFileId, destinationPath opcional)", operationId = "downloadFile" } },
                ["/api/v1/files/{remoteFileId}/move"] = new { post = new { summary = "Move arquivo/pasta para outra pasta e/ou renomeia (targetFolderId, newName)", operationId = "moveFile" } },
                ["/api/v1/files/{remoteFileId}/trash"] = new { post = new { summary = "Move arquivo ou pasta para a Lixeira do Google Drive", operationId = "trashFile" } },
                ["/api/v1/files/{remoteFileId}/restore"] = new { post = new { summary = "Restaura item da Lixeira do Google Drive", operationId = "restoreFile" } },
                ["/api/v1/files/{remoteFileId}"] = new { delete = new { summary = "Move para a Lixeira ou exclui permanentemente com ?permanent=true", operationId = "deleteFile" } },
                ["/api/v1/transfers"] = new { get = new { summary = "Consulta fila de transferencias ativas e recentes", operationId = "getTransfers" } },
                ["/api/v1/logs"] = new { get = new { summary = "Consulta logs recentes de atividade e sincronizacao (q, limit)", operationId = "getLogs" } },
                ["/api/v1/openapi.json"] = new { get = new { summary = "Esquema OpenAPI 3.0.3 para autodescoberta por agentes de IA", operationId = "getOpenApiSpec" } }
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    internal static bool IsAllowedLoopbackRequest(
        string? hostHeader,
        string? originHeader,
        string? refererHeader = null,
        string? secFetchSiteHeader = null)
    {
        if (!string.IsNullOrWhiteSpace(secFetchSiteHeader) &&
            string.Equals(secFetchSiteHeader.Trim(), "cross-site", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(hostHeader) && !IsLoopbackAuthority(hostHeader))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(originHeader))
        {
            if (!Uri.TryCreate(originHeader, UriKind.Absolute, out var originUri) ||
                !IsLoopbackHostName(originUri.Host))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(refererHeader))
        {
            if (!Uri.TryCreate(refererHeader, UriKind.Absolute, out var refererUri) ||
                !IsLoopbackHostName(refererUri.Host))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsSafeLocalApiFilePath(string fullPath, out string? blockReason)
    {
        blockReason = null;
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            blockReason = "File path cannot be empty.";
            return false;
        }

        var normalized = Path.GetFullPath(fullPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        static string WithTrailingSep(string dir)
        {
            var full = Path.GetFullPath(dir);
            return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
        }

        var protectedDirs = new[]
        {
            AppPaths.ConfigDirectory,
            AppPaths.DataDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gnupg"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications")
        };

        foreach (var dir in protectedDirs)
        {
            var prefix = WithTrailingSep(dir);
            if (string.Equals(normalized, Path.GetFullPath(dir), comparison) ||
                normalized.StartsWith(prefix, comparison))
            {
                blockReason = "Access to protected application or system credentials directory is not allowed.";
                return false;
            }
        }

        var fileName = Path.GetFileName(normalized);
        if (fileName is ".bashrc" or ".bash_profile" or ".profile" or ".zshrc")
        {
            blockReason = "Modifying or reading shell startup files via local API is not allowed.";
            return false;
        }

        return true;
    }

    private static bool IsLoopbackAuthority(string hostHeader)
    {
        var candidate = $"http://{hostHeader.Trim()}";
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && IsLoopbackHostName(uri.Host);
    }

    private static bool IsLoopbackHostName(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        (string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase));

    private static IResult NotConnectedProblem() =>
        Results.Problem(
            title: "Google Drive account not connected",
            detail: "Connect a Google account in TundraDrive before calling this endpoint.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static DriveNodeDto MapNodeDto(RemoteDriveNode node) => new(
        node.Id,
        node.Name,
        node.MimeType,
        node.IsFolder,
        node.SizeBytes,
        node.Trashed,
        node.ParentIds,
        node.ModifiedTimeUtc);

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
