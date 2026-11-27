using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using TundraDrive.Core.Drive;
using TundraDrive.Core.Security;
using TundraDrive.Core.Sync;

namespace TundraDrive.Desktop.Services;

public enum PreviewKind
{
    None = 0,
    Image = 1,
    Video = 2,
    Audio = 3,
    Pdf = 4,
    Text = 5,
    Unsupported = 6
}

public sealed record FilePreviewResult(
    PreviewKind Kind,
    string LocalFilePath,
    Bitmap? VisualBitmap = null,
    string? TextContent = null,
    string? MediaInfo = null,
    int PageNumber = 1,
    int TotalPages = 1);

public sealed class FilePreviewService : IDisposable
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico", ".svg", ".avif", ".heic", ".tiff"
    };

    private static readonly HashSet<string> NativeBitmapExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".m4v", ".wmv", ".3gp", ".mpeg", ".mpg"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".opus", ".wma"
    };

    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".doc", ".xlsx", ".xls", ".xlsm", ".pptx", ".ppt", ".odt", ".ods", ".odp", ".rtf"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".cs", ".py", ".js", ".ts", ".jsx", ".tsx",
        ".html", ".css", ".scss", ".por", ".sh", ".bash", ".yml", ".yaml", ".csv",
        ".tsv", ".log", ".ini", ".conf", ".cfg", ".properties", ".toml", ".env",
        ".sql", ".axaml", ".csproj", ".sln", ".gitignore", ".tex", ".bib", ".org",
        ".rst", ".srt", ".vtt", ".c", ".cpp", ".h", ".hpp", ".java", ".kt", ".go",
        ".rs", ".rb", ".php", ".swift", ".dart", ".vue", ".svelte", ".ps1", ".bat", ".cmd"
    };

    private const string GoogleVidsMimeType = "application/vnd.google-apps.vid";
    private const string GoogleDrawingMimeType = "application/vnd.google-apps.drawing";
    private const long MaxRemoteMediaPreviewBytes = 100L * 1024 * 1024;

    private static readonly HashSet<string> NonExportableGoogleWorkspaceMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.google-apps.shortcut",
        "application/vnd.google-apps.form",
        "application/vnd.google-apps.site",
        "application/vnd.google-apps.map",
        "application/vnd.google-apps.fusiontable",
        "application/vnd.google-apps.jam",
        "application/vnd.google-apps.drive-sdk",
        "application/vnd.google-apps.third_party_shortcut"
    };

    private readonly IGoogleDriveClient _driveClient;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheKeyLocks = new(StringComparer.Ordinal);
    private Process? _audioProcess;

    public bool IsAudioPlaying => _audioProcess is { HasExited: false };

    public FilePreviewService(IGoogleDriveClient driveClient)
    {
        _driveClient = driveClient;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var rootCacheDir = Path.Combine(home, ".cache", "custodia");
        _cacheDirectory = Path.Combine(rootCacheDir, "preview");
        PosixPermissionHelper.EnsurePrivateDirectory(rootCacheDir);
        PosixPermissionHelper.EnsurePrivateDirectory(_cacheDirectory);
    }

    public async Task<FilePreviewResult> BuildPreviewAsync(
        string fileName,
        string? localFullPath,
        string remoteFileId,
        bool isGoogleWorkspaceDoc,
        string? mimeType,
        int pdfPage = 1,
        CancellationToken cancellationToken = default,
        long sizeBytes = 0,
        string? thumbnailLink = null)
    {
        if (isGoogleWorkspaceDoc && IsNonExportableWorkspaceMime(mimeType))
        {
            return new FilePreviewResult(PreviewKind.Unsupported, localFullPath ?? string.Empty);
        }

        var ext = ResolveEffectiveExtension(fileName, mimeType, isGoogleWorkspaceDoc);
        var resolvedPath = localFullPath;

        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            if (string.IsNullOrWhiteSpace(remoteFileId) || !_driveClient.IsConnected)
            {
                return new FilePreviewResult(PreviewKind.Unsupported, string.Empty);
            }

            var isLargeRemoteMedia = !isGoogleWorkspaceDoc &&
                sizeBytes > MaxRemoteMediaPreviewBytes &&
                (VideoExtensions.Contains(ext) || AudioExtensions.Contains(ext));

            if (isLargeRemoteMedia)
            {
                var mediaThumb = await TryLoadGridThumbnailAsync(
                    fileName,
                    localFullPath,
                    remoteFileId,
                    sizeBytes,
                    cancellationToken,
                    thumbnailLink);
                var kind = VideoExtensions.Contains(ext) ? PreviewKind.Video : PreviewKind.Audio;
                return new FilePreviewResult(kind, string.Empty, VisualBitmap: mediaThumb);
            }

            var safeRemoteId = DriveSyncService.SanitizeRemoteSegment(remoteFileId);
            var keyGate = _cacheKeyLocks.GetOrAdd(safeRemoteId, _ => new SemaphoreSlim(1, 1));
            await keyGate.WaitAsync(cancellationToken);
            try
            {
                if (isGoogleWorkspaceDoc)
                {
                    try
                    {
                        var exportResult = await ResolveOrExportWorkspacePreviewFileAsync(
                            remoteFileId,
                            safeRemoteId,
                            mimeType,
                            ext,
                            cancellationToken);
                        resolvedPath = exportResult.Path;
                        ext = exportResult.Extension;
                    }
                    catch (Google.GoogleApiException) when (!cancellationToken.IsCancellationRequested)
                    {
                        var thumb = await TryLoadGridThumbnailAsync(
                            fileName,
                            localFullPath,
                            remoteFileId,
                            sizeBytes,
                            cancellationToken,
                            thumbnailLink);
                        if (thumb is not null)
                        {
                            var fallbackKind = string.Equals(mimeType, GoogleVidsMimeType, StringComparison.OrdinalIgnoreCase)
                                ? PreviewKind.Video
                                : PreviewKind.Image;
                            return new FilePreviewResult(fallbackKind, string.Empty, VisualBitmap: thumb);
                        }

                        return new FilePreviewResult(PreviewKind.Unsupported, string.Empty);
                    }
                }
                else
                {
                    var safeCacheName = $"{safeRemoteId}{(string.IsNullOrWhiteSpace(ext) ? ".bin" : ext)}";
                    resolvedPath = Path.Combine(_cacheDirectory, safeCacheName);

                    if (!File.Exists(resolvedPath) || new FileInfo(resolvedPath).Length == 0)
                    {
                        await _driveClient.DownloadFileAsync(remoteFileId, resolvedPath, null, cancellationToken);
                    }
                }
            }
            finally
            {
                keyGate.Release();
            }
        }

        var cacheKey = !string.IsNullOrWhiteSpace(remoteFileId) && string.IsNullOrWhiteSpace(localFullPath)
            ? DriveSyncService.SanitizeRemoteSegment(remoteFileId)
            : ComputeFileKey(resolvedPath);

        if (ImageExtensions.Contains(ext))
        {
            var bitmap = await LoadImageBitmapAsync(resolvedPath, ext, cacheKey, cancellationToken);
            return new FilePreviewResult(
                bitmap is not null ? PreviewKind.Image : PreviewKind.Unsupported,
                resolvedPath,
                VisualBitmap: bitmap);
        }

        if (VideoExtensions.Contains(ext))
        {
            var frameBitmap = await ExtractVideoFrameAsync(resolvedPath, cacheKey, cancellationToken);
            var info = await ProbeMediaDurationAsync(resolvedPath, cancellationToken);
            return new FilePreviewResult(
                PreviewKind.Video,
                resolvedPath,
                VisualBitmap: frameBitmap,
                MediaInfo: info);
        }

        if (AudioExtensions.Contains(ext))
        {
            var waveBitmap = await GenerateAudioWaveformAsync(resolvedPath, cacheKey, cancellationToken);
            var info = await ProbeMediaDurationAsync(resolvedPath, cancellationToken);
            return new FilePreviewResult(
                PreviewKind.Audio,
                resolvedPath,
                VisualBitmap: waveBitmap,
                MediaInfo: info);
        }

        if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var totalPages = await GetPdfPageCountAsync(resolvedPath, cancellationToken);
            var page = totalPages > 0 ? Math.Clamp(pdfPage, 1, totalPages) : Math.Max(1, pdfPage);
            var pdfBitmap = await RenderPdfPageAsync(resolvedPath, cacheKey, page, cancellationToken);
            return new FilePreviewResult(
                pdfBitmap is not null ? PreviewKind.Pdf : PreviewKind.Unsupported,
                resolvedPath,
                VisualBitmap: pdfBitmap,
                PageNumber: page,
                TotalPages: Math.Max(1, totalPages));
        }

        if (OfficeExtensions.Contains(ext))
        {
            var convertedPdf = await ConvertOfficeToPdfAsync(resolvedPath, cacheKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(convertedPdf) && File.Exists(convertedPdf))
            {
                var totalPages = await GetPdfPageCountAsync(convertedPdf, cancellationToken);
                var page = totalPages > 0 ? Math.Clamp(pdfPage, 1, totalPages) : Math.Max(1, pdfPage);
                var pdfBitmap = await RenderPdfPageAsync(convertedPdf, $"{cacheKey}_office", page, cancellationToken);
                return new FilePreviewResult(
                    pdfBitmap is not null ? PreviewKind.Pdf : PreviewKind.Unsupported,
                    resolvedPath,
                    VisualBitmap: pdfBitmap,
                    PageNumber: page,
                    TotalPages: Math.Max(1, totalPages));
            }

            if (string.Equals(ext, ".rtf", StringComparison.OrdinalIgnoreCase))
            {
                var rtfText = await ReadTextPreviewAsync(resolvedPath, cancellationToken);
                return new FilePreviewResult(PreviewKind.Text, resolvedPath, TextContent: rtfText);
            }

            return new FilePreviewResult(PreviewKind.Unsupported, resolvedPath);
        }

        if (TextExtensions.Contains(ext) || IsLikelyUtf8TextFile(resolvedPath))
        {
            var content = await ReadTextPreviewAsync(resolvedPath, cancellationToken);
            return new FilePreviewResult(PreviewKind.Text, resolvedPath, TextContent: content);
        }

        return new FilePreviewResult(PreviewKind.Unsupported, resolvedPath);
    }

    public async Task<Bitmap?> TryGetCachedThumbnailBitmapAsync(
        string? remoteFileId,
        string? localFullPath,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName);
        if (!ImageExtensions.Contains(ext) && !VideoExtensions.Contains(ext))
        {
            return null;
        }

        var safeThumbId = string.IsNullOrWhiteSpace(remoteFileId) ? string.Empty : DriveSyncService.SanitizeRemoteSegment(remoteFileId);
        var thumbKey = string.IsNullOrWhiteSpace(safeThumbId)
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(localFullPath ?? fileName))).ToLowerInvariant()[..16]
            : safeThumbId;

        var cachedThumb = Path.Combine(_cacheDirectory, $"{thumbKey}_grid.png");
        if (File.Exists(cachedThumb))
        {
            try
            {
                return await Task.Run(() =>
                {
                    using var cachedStream = File.OpenRead(cachedThumb);
                    return Bitmap.DecodeToWidth(cachedStream, 320);
                }, cancellationToken);
            }
            catch
            {
            }
        }

        return null;
    }

    public async Task<Bitmap?> TryLoadGridThumbnailAsync(
        string fileName,
        string? localFullPath,
        string remoteFileId,
        long sizeBytes,
        CancellationToken cancellationToken = default,
        string? thumbnailLink = null)
    {
        var ext = Path.GetExtension(fileName);
        var isImage = ImageExtensions.Contains(ext);
        var isVideo = VideoExtensions.Contains(ext);
        var hasThumbLink = !string.IsNullOrWhiteSpace(thumbnailLink);
        if (!isImage && !isVideo && !hasThumbLink)
        {
            return null;
        }

        var safeThumbId = string.IsNullOrWhiteSpace(remoteFileId) ? string.Empty : DriveSyncService.SanitizeRemoteSegment(remoteFileId);
        var thumbKey = string.IsNullOrWhiteSpace(safeThumbId)
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(localFullPath ?? fileName))).ToLowerInvariant()[..16]
            : safeThumbId;

        var cachedThumb = Path.Combine(_cacheDirectory, $"{thumbKey}_grid.png");
        if (File.Exists(cachedThumb))
        {
            try
            {
                return await Task.Run(() =>
                {
                    using var cachedStream = File.OpenRead(cachedThumb);
                    return Bitmap.DecodeToWidth(cachedStream, 160);
                }, cancellationToken);
            }
            catch
            {
            }
        }

        if (hasThumbLink && !string.IsNullOrWhiteSpace(remoteFileId) && _driveClient is IResumableDriveClient resumableThumbClient && _driveClient.IsConnected)
        {
            var thumbBytes = await resumableThumbClient.DownloadThumbnailAsync(remoteFileId, thumbnailLink, cancellationToken);
            if (thumbBytes is { Length: > 0 })
            {
                try
                {
                    var tmpThumb = $"{cachedThumb}.{Guid.NewGuid():N}.tmp";
                    await File.WriteAllBytesAsync(tmpThumb, thumbBytes, cancellationToken);
                    File.Move(tmpThumb, cachedThumb, overwrite: true);
                }
                catch
                {
                }

                try
                {
                    return await Task.Run(() =>
                    {
                        using var mem = new MemoryStream(thumbBytes, writable: false);
                        return Bitmap.DecodeToWidth(mem, 160);
                    }, cancellationToken);
                }
                catch
                {
                }
            }
        }

        if (!isImage && !isVideo)
        {
            return null;
        }

        var sourcePath = localFullPath;
        var downloadedTempForThumb = false;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            var maxRemoteBytes = isImage ? 4L * 1024 * 1024 : 10L * 1024 * 1024;
            if (string.IsNullOrWhiteSpace(remoteFileId) || !_driveClient.IsConnected || sizeBytes > maxRemoteBytes)
            {
                return null;
            }

            // Use an isolated thumbnail cache key lock so grid loading never stalls active previews.
            var thumbSourceSuffix = sizeBytes > 2 * 1024 * 1024 ? "_thumbsrc" : string.Empty;
            var cachedSource = Path.Combine(_cacheDirectory, $"{safeThumbId}{thumbSourceSuffix}{ext}");
            var keyGate = _cacheKeyLocks.GetOrAdd($"thumb:{safeThumbId}{thumbSourceSuffix}", _ => new SemaphoreSlim(1, 1));
            await keyGate.WaitAsync(cancellationToken);
            try
            {
                if (!File.Exists(cachedSource) || new FileInfo(cachedSource).Length == 0)
                {
                    await _driveClient.DownloadFileAsync(remoteFileId, cachedSource, null, cancellationToken);
                }

                downloadedTempForThumb = sizeBytes > 2 * 1024 * 1024;
            }
            finally
            {
                keyGate.Release();
            }

            sourcePath = cachedSource;
        }

        try
        {
            if (isImage)
            {
                if (NativeBitmapExtensions.Contains(ext))
                {
                    return await Task.Run(() =>
                    {
                        using var fs = File.OpenRead(sourcePath);
                        return Bitmap.DecodeToWidth(fs, 160);
                    }, cancellationToken);
                }

                await RunToolAsync("magick", [sourcePath, "-resize", "140x140>", cachedThumb], cancellationToken);
            }
            else if (isVideo)
            {
                await RunToolAsync("ffmpeg", ["-y", "-ss", "00:00:02", "-i", sourcePath, "-vframes", "1", "-vf", "scale=140:-1", cachedThumb], cancellationToken);
                if (!File.Exists(cachedThumb))
                {
                    await RunToolAsync("ffmpeg", ["-y", "-ss", "00:00:00", "-i", sourcePath, "-vframes", "1", "-vf", "scale=140:-1", cachedThumb], cancellationToken);
                }
            }

            if (File.Exists(cachedThumb))
            {
                return await Task.Run(() =>
                {
                    using var fs = File.OpenRead(cachedThumb);
                    return Bitmap.DecodeToWidth(fs, 160);
                }, cancellationToken);
            }
        }
        catch
        {
        }
        finally
        {
            if (downloadedTempForThumb && File.Exists(cachedThumb) && !string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            {
                try
                {
                    File.Delete(sourcePath);
                }
                catch
                {
                }
            }
        }

        return null;
    }

    public void PlayAudio(string localFilePath, Action onPlaybackEnded)
    {
        StopAudio();
        if (!File.Exists(localFilePath))
        {
            return;
        }

        try
        {
            var psi = new ProcessStartInfo("ffplay")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-nodisp");
            psi.ArgumentList.Add("-autoexit");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("quiet");
            psi.ArgumentList.Add(localFilePath);

            var proc = Process.Start(psi);
            if (proc is not null)
            {
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) => onPlaybackEnded();
                _audioProcess = proc;
            }
        }
        catch
        {
            onPlaybackEnded();
        }
    }

    public void StopAudio()
    {
        try
        {
            if (_audioProcess is { HasExited: false })
            {
                _audioProcess.Kill(entireProcessTree: true);
            }
            _audioProcess?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _audioProcess = null;
        }
    }

    public static void PlayVideoExternal(string localFilePath, string title)
    {
        if (!File.Exists(localFilePath))
        {
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var xdg = new ProcessStartInfo("xdg-open")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                xdg.ArgumentList.Add(localFilePath);
                using var xdgProc = Process.Start(xdg);
                if (xdgProc is not null)
                {
                    return;
                }
            }
            catch
            {
            }
        }

        try
        {
            var psi = new ProcessStartInfo("ffplay")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-autoexit");
            psi.ArgumentList.Add("-window_title");
            psi.ArgumentList.Add($"Custódia — {title}");
            psi.ArgumentList.Add(localFilePath);
            using var proc = Process.Start(psi);
        }
        catch
        {
        }
    }

    private async Task<Bitmap?> LoadImageBitmapAsync(string filePath, string ext, string key, CancellationToken ct)
    {
        try
        {
            if (NativeBitmapExtensions.Contains(ext))
            {
                try
                {
                    return await Task.Run(() =>
                    {
                        using var fs = File.OpenRead(filePath);
                        return Bitmap.DecodeToWidth(fs, 960);
                    }, ct);
                }
                catch when (!ct.IsCancellationRequested)
                {
                    // Fall back to external conversion if format differs from file extension.
                }
            }

            var pngPath = Path.Combine(_cacheDirectory, $"{key}_img.png");
            if (!File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
            {
                await RunToolAsync("magick", [filePath, "-resize", "960x960>", pngPath], ct);
                if (!File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
                {
                    await RunToolAsync("ffmpeg", ["-y", "-i", filePath, "-vf", "scale='min(960,iw)':-1", "-frames:v", "1", pngPath], ct);
                }
            }

            if (File.Exists(pngPath) && new FileInfo(pngPath).Length > 0)
            {
                return await Task.Run(() =>
                {
                    using var fs = File.OpenRead(pngPath);
                    return Bitmap.DecodeToWidth(fs, 960);
                }, ct);
            }
        }
        catch
        {
        }

        return null;
    }

    private async Task<Bitmap?> ExtractVideoFrameAsync(string filePath, string key, CancellationToken ct)
    {
        var thumbPath = Path.Combine(_cacheDirectory, $"{key}_vthumb.png");
        if (!File.Exists(thumbPath) || new FileInfo(thumbPath).Length == 0)
        {
            await RunToolAsync("ffmpeg", ["-y", "-ss", "00:00:02", "-i", filePath, "-vframes", "1", "-vf", "scale=720:-1", thumbPath], ct);
            if (!File.Exists(thumbPath) || new FileInfo(thumbPath).Length == 0)
            {
                await RunToolAsync("ffmpeg", ["-y", "-ss", "00:00:00", "-i", filePath, "-vframes", "1", "-vf", "scale=720:-1", thumbPath], ct);
            }
        }

        if (File.Exists(thumbPath) && new FileInfo(thumbPath).Length > 0)
        {
            return await Task.Run(() =>
            {
                using var fs = File.OpenRead(thumbPath);
                return Bitmap.DecodeToWidth(fs, 720);
            }, ct);
        }

        return null;
    }

    private async Task<Bitmap?> GenerateAudioWaveformAsync(string filePath, string key, CancellationToken ct)
    {
        var wavePath = Path.Combine(_cacheDirectory, $"{key}_wave.png");
        if (!File.Exists(wavePath) || new FileInfo(wavePath).Length == 0)
        {
            await RunToolAsync("ffmpeg",
            [
                "-y", "-i", filePath,
                "-filter_complex", "showwavespic=s=640x160:colors=#3B82F6|#60A5FA",
                "-frames:v", "1", wavePath
            ], ct);
        }

        if (File.Exists(wavePath) && new FileInfo(wavePath).Length > 0)
        {
            return await Task.Run(() =>
            {
                using var fs = File.OpenRead(wavePath);
                return Bitmap.DecodeToWidth(fs, 640);
            }, ct);
        }

        return null;
    }

    private async Task<Bitmap?> RenderPdfPageAsync(string filePath, string key, int page, CancellationToken ct)
    {
        var prefix = Path.Combine(_cacheDirectory, $"{key}_p{page}");
        var expectedPng = $"{prefix}.png";

        if (!File.Exists(expectedPng) || new FileInfo(expectedPng).Length == 0)
        {
            await RunToolAsync("pdftoppm",
            [
                "-png", "-r", "120", "-singlefile",
                "-f", page.ToString(), "-l", page.ToString(),
                filePath, prefix
            ], ct);

            if (!File.Exists(expectedPng) || new FileInfo(expectedPng).Length == 0)
            {
                await RunToolAsync("gs",
                [
                    "-q", "-dNOPAUSE", "-dBATCH", "-sDEVICE=png16m", "-r120",
                    $"-dFirstPage={page}", $"-dLastPage={page}",
                    $"-sOutputFile={expectedPng}",
                    filePath
                ], ct);
            }
        }

        if (File.Exists(expectedPng) && new FileInfo(expectedPng).Length > 0)
        {
            try
            {
                return await Task.Run(() =>
                {
                    using var fs = File.OpenRead(expectedPng);
                    return Bitmap.DecodeToWidth(fs, 960);
                }, ct);
            }
            catch
            {
            }
        }

        return null;
    }

    private static string ResolveEffectiveExtension(string fileName, string? mimeType, bool isGoogleWorkspaceDoc)
    {
        if (isGoogleWorkspaceDoc)
        {
            if (string.Equals(mimeType, GoogleVidsMimeType, StringComparison.OrdinalIgnoreCase))
            {
                return ".mp4";
            }

            if (string.Equals(mimeType, GoogleDrawingMimeType, StringComparison.OrdinalIgnoreCase))
            {
                return ".png";
            }

            return string.Equals(mimeType, "application/vnd.google-apps.script", StringComparison.OrdinalIgnoreCase)
                ? ".json"
                : ".pdf";
        }

        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrWhiteSpace(ext) &&
            (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase) ||
             ImageExtensions.Contains(ext) ||
             VideoExtensions.Contains(ext) ||
             AudioExtensions.Contains(ext) ||
             OfficeExtensions.Contains(ext) ||
             TextExtensions.Contains(ext)))
        {
            return ext;
        }

        if (!string.IsNullOrWhiteSpace(mimeType))
        {
            if (string.Equals(mimeType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                return ".pdf";
            }

            if (string.Equals(mimeType, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase))
            {
                return ".docx";
            }

            if (string.Equals(mimeType, "application/msword", StringComparison.OrdinalIgnoreCase))
            {
                return ".doc";
            }

            if (string.Equals(mimeType, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase))
            {
                return ".xlsx";
            }

            if (string.Equals(mimeType, "application/vnd.ms-excel", StringComparison.OrdinalIgnoreCase))
            {
                return ".xls";
            }

            if (string.Equals(mimeType, "application/vnd.openxmlformats-officedocument.presentationml.presentation", StringComparison.OrdinalIgnoreCase))
            {
                return ".pptx";
            }

            if (string.Equals(mimeType, "application/vnd.ms-powerpoint", StringComparison.OrdinalIgnoreCase))
            {
                return ".ppt";
            }

            if (string.Equals(mimeType, "application/vnd.oasis.opendocument.text", StringComparison.OrdinalIgnoreCase))
            {
                return ".odt";
            }

            if (string.Equals(mimeType, "application/vnd.oasis.opendocument.spreadsheet", StringComparison.OrdinalIgnoreCase))
            {
                return ".ods";
            }

            if (string.Equals(mimeType, "application/vnd.oasis.opendocument.presentation", StringComparison.OrdinalIgnoreCase))
            {
                return ".odp";
            }

            if (string.Equals(mimeType, "application/rtf", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mimeType, "text/rtf", StringComparison.OrdinalIgnoreCase))
            {
                return ".rtf";
            }

            if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return mimeType.Contains("png", StringComparison.OrdinalIgnoreCase) ? ".png"
                    : mimeType.Contains("webp", StringComparison.OrdinalIgnoreCase) ? ".webp"
                    : mimeType.Contains("gif", StringComparison.OrdinalIgnoreCase) ? ".gif"
                    : ".jpg";
            }

            if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                return ".mp4";
            }

            if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            {
                return ".mp3";
            }

            if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mimeType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mimeType, "application/xml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mimeType, "application/javascript", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mimeType, "application/x-sh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mimeType, "application/x-yaml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mimeType, "application/yaml", StringComparison.OrdinalIgnoreCase))
            {
                return ".txt";
            }
        }

        return ext;
    }

    internal static bool IsNonExportableWorkspaceMime(string? mimeType) =>
        !string.IsNullOrWhiteSpace(mimeType) && NonExportableGoogleWorkspaceMimes.Contains(mimeType);

    private async Task<(string Path, string Extension)> ResolveOrExportWorkspacePreviewFileAsync(
        string remoteFileId,
        string safeRemoteId,
        string? mimeType,
        string initialExtension,
        CancellationToken cancellationToken)
    {
        if (string.Equals(mimeType, GoogleVidsMimeType, StringComparison.OrdinalIgnoreCase))
        {
            var vidPath = Path.Combine(_cacheDirectory, $"{safeRemoteId}.mp4");
            if (!File.Exists(vidPath) || new FileInfo(vidPath).Length == 0)
            {
                await _driveClient.DownloadFileAsync(remoteFileId, vidPath, null, cancellationToken);
            }

            return (vidPath, ".mp4");
        }

        if (string.Equals(mimeType, GoogleDrawingMimeType, StringComparison.OrdinalIgnoreCase))
        {
            var drawingPath = Path.Combine(_cacheDirectory, $"{safeRemoteId}.png");
            if (!File.Exists(drawingPath) || new FileInfo(drawingPath).Length == 0)
            {
                await _driveClient.ExportFileAsync(remoteFileId, "image/png", drawingPath, null, cancellationToken);
            }

            return (drawingPath, ".png");
        }

        if (string.Equals(mimeType, "application/vnd.google-apps.script", StringComparison.OrdinalIgnoreCase))
        {
            var scriptPath = Path.Combine(_cacheDirectory, $"{safeRemoteId}.json");
            if (!File.Exists(scriptPath) || new FileInfo(scriptPath).Length == 0)
            {
                await _driveClient.ExportFileAsync(remoteFileId, "application/vnd.google-apps.script+json", scriptPath, null, cancellationToken);
            }

            return (scriptPath, ".json");
        }

        var pdfPath = Path.Combine(_cacheDirectory, $"{safeRemoteId}{initialExtension}");
        if (File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0)
        {
            return (pdfPath, ".pdf");
        }

        var fallbackTextPath = Path.Combine(_cacheDirectory, $"{safeRemoteId}.txt");
        if (File.Exists(fallbackTextPath) && new FileInfo(fallbackTextPath).Length > 0)
        {
            return (fallbackTextPath, ".txt");
        }

        var fallbackCsvPath = Path.Combine(_cacheDirectory, $"{safeRemoteId}.csv");
        if (File.Exists(fallbackCsvPath) && new FileInfo(fallbackCsvPath).Length > 0)
        {
            return (fallbackCsvPath, ".csv");
        }

        try
        {
            await _driveClient.ExportFileAsync(remoteFileId, "application/pdf", pdfPath, null, cancellationToken);
            return (pdfPath, ".pdf");
        }
        catch when (!cancellationToken.IsCancellationRequested &&
                    (string.Equals(mimeType, "application/vnd.google-apps.document", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(mimeType, "application/vnd.google-apps.spreadsheet", StringComparison.OrdinalIgnoreCase)))
        {
            // Fallback to plain text/CSV when Google Docs or Sheets hit PDF export size limits.
            var textMime = string.Equals(mimeType, "application/vnd.google-apps.spreadsheet", StringComparison.OrdinalIgnoreCase)
                ? "text/csv"
                : "text/plain";
            var ext = textMime == "text/csv" ? ".csv" : ".txt";
            var textPath = Path.Combine(_cacheDirectory, $"{safeRemoteId}{ext}");
            await _driveClient.ExportFileAsync(remoteFileId, textMime, textPath, null, cancellationToken);
            return (textPath, ext);
        }
    }

    private static bool IsLikelyUtf8TextFile(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length == 0 || info.Length > 1024 * 1024)
            {
                return false;
            }

            Span<byte> sample = stackalloc byte[(int)Math.Min(info.Length, 4096)];
            using var fs = File.OpenRead(filePath);
            var read = fs.Read(sample);
            if (read <= 0)
            {
                return false;
            }

            for (var i = 0; i < read; i++)
            {
                var b = sample[i];
                if (b == 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeFileKey(string filePath)
    {
        var fingerprint = filePath;
        try
        {
            var info = new FileInfo(filePath);
            if (info.Exists)
            {
                fingerprint = $"{filePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            }
        }
        catch
        {
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint)))
            .ToLowerInvariant()[..16];
    }

    private static async Task<int> GetPdfPageCountAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("pdfinfo")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(filePath);

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return 0;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(timeoutCts.Token);
            await proc.WaitForExitAsync(timeoutCts.Token);
            var output = await stdoutTask;
            await stderrTask;

            foreach (var line in output.Split('\n'))
            {
                if (line.StartsWith("Pages:", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(line["Pages:".Length..].Trim(), out var pages) &&
                    pages > 0)
                {
                    return pages;
                }
            }
        }
        catch
        {
        }

        return 0;
    }

    private async Task<string?> ConvertOfficeToPdfAsync(string filePath, string key, CancellationToken ct)
    {
        var outDir = Path.Combine(_cacheDirectory, "office");
        Directory.CreateDirectory(outDir);
        var targetPdf = Path.Combine(outDir, $"{key}.pdf");
        if (File.Exists(targetPdf) && new FileInfo(targetPdf).Length > 0)
        {
            return targetPdf;
        }

        var loProfile = Path.Combine(Path.GetTempPath(), $"custodia-lo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(loProfile);

        try
        {
            var profileUri = new Uri(loProfile).AbsoluteUri;
            await RunToolAsync("soffice",
            [
                "--invisible",
                "--nodefault",
                "--nofirststartwizard",
                "--headless",
                "--norestore",
                $"-env:UserInstallation={profileUri}",
                "--convert-to", "pdf",
                "--outdir", outDir,
                filePath
            ], ct);

            var generatedName = Path.ChangeExtension(Path.GetFileName(filePath), ".pdf");
            var generatedPath = Path.Combine(outDir, generatedName);
            if (File.Exists(generatedPath) && new FileInfo(generatedPath).Length > 0)
            {
                if (!string.Equals(generatedPath, targetPdf, StringComparison.Ordinal))
                {
                    File.Move(generatedPath, targetPdf, overwrite: true);
                }

                return targetPdf;
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(loProfile))
                {
                    Directory.Delete(loProfile, recursive: true);
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static async Task<string> ReadTextPreviewAsync(string filePath, CancellationToken ct)
    {
        const int maxChars = 65536;
        using var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[maxChars];
        var read = await reader.ReadAsync(buffer, ct);
        var text = new string(buffer, 0, read);
        return read == maxChars ? $"{text}\n\n[...]" : text;
    }

    private static async Task<string?> ProbeMediaDurationAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("ffprobe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-show_entries");
            psi.ArgumentList.Add("format=duration");
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
            psi.ArgumentList.Add(filePath);

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(timeoutCts.Token);
            await proc.WaitForExitAsync(timeoutCts.Token);
            var output = (await stdoutTask).Trim();
            await stderrTask;

            if (double.TryParse(output, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs))
            {
                var ts = TimeSpan.FromSeconds(secs);
                return ts.TotalHours >= 1
                    ? ts.ToString(@"hh\:mm\:ss")
                    : ts.ToString(@"mm\:ss");
            }
        }
        catch
        {
        }

        return null;
    }

    private static async Task RunToolAsync(string executable, string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));
                try
                {
                    var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                    var stderrTask = proc.StandardError.ReadToEndAsync(timeoutCts.Token);
                    await proc.WaitForExitAsync(timeoutCts.Token);
                    await Task.WhenAll(stdoutTask, stderrTask);
                }
                catch
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                    }
                    throw;
                }
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        StopAudio();
    }
}
