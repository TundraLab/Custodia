using System.Diagnostics;
using Avalonia.Media.Imaging;
using TundraDrive.Core.Drive;

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
        ".log", ".ini", ".conf", ".sql", ".axaml", ".csproj", ".sln", ".gitignore"
    };

    private readonly IGoogleDriveClient _driveClient;
    private readonly string _cacheDirectory;
    private Process? _audioProcess;

    public bool IsAudioPlaying => _audioProcess is { HasExited: false };

    public FilePreviewService(IGoogleDriveClient driveClient)
    {
        _driveClient = driveClient;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _cacheDirectory = Path.Combine(home, ".cache", "tundradrive", "preview");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<FilePreviewResult> BuildPreviewAsync(
        string fileName,
        string? localFullPath,
        string remoteFileId,
        bool isGoogleWorkspaceDoc,
        string? mimeType,
        int pdfPage = 1,
        CancellationToken cancellationToken = default)
    {
        var ext = ResolveEffectiveExtension(fileName, mimeType, isGoogleWorkspaceDoc);
        var resolvedPath = localFullPath;

        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            if (string.IsNullOrWhiteSpace(remoteFileId) || !_driveClient.IsConnected)
            {
                return new FilePreviewResult(PreviewKind.Unsupported, string.Empty);
            }

            var safeCacheName = $"{remoteFileId}{(string.IsNullOrWhiteSpace(ext) ? ".bin" : ext)}";
            resolvedPath = Path.Combine(_cacheDirectory, safeCacheName);

            if (!File.Exists(resolvedPath) || new FileInfo(resolvedPath).Length == 0)
            {
                if (isGoogleWorkspaceDoc)
                {
                    await _driveClient.ExportFileAsync(remoteFileId, "application/pdf", resolvedPath, null, cancellationToken);
                }
                else
                {
                    await _driveClient.DownloadFileAsync(remoteFileId, resolvedPath, null, cancellationToken);
                }
            }
        }

        var cacheKey = !string.IsNullOrWhiteSpace(remoteFileId) ? remoteFileId : ComputeFileKey(resolvedPath);

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

            return new FilePreviewResult(PreviewKind.Unsupported, resolvedPath);
        }

        if (TextExtensions.Contains(ext))
        {
            var content = await ReadTextPreviewAsync(resolvedPath, cancellationToken);
            return new FilePreviewResult(PreviewKind.Text, resolvedPath, TextContent: content);
        }

        return new FilePreviewResult(PreviewKind.Unsupported, resolvedPath);
    }

    public async Task<Bitmap?> TryLoadGridThumbnailAsync(
        string fileName,
        string? localFullPath,
        string remoteFileId,
        long sizeBytes,
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName);
        var isImage = ImageExtensions.Contains(ext);
        var isVideo = VideoExtensions.Contains(ext);
        if (!isImage && !isVideo)
        {
            return null;
        }

        var thumbKey = string.IsNullOrWhiteSpace(remoteFileId)
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(localFullPath ?? fileName))).ToLowerInvariant()[..16]
            : remoteFileId;

        var cachedThumb = Path.Combine(_cacheDirectory, $"{thumbKey}_grid.png");
        if (File.Exists(cachedThumb))
        {
            try
            {
                await using var cachedStream = File.OpenRead(cachedThumb);
                return Bitmap.DecodeToWidth(cachedStream, 128);
            }
            catch
            {
            }
        }

        var sourcePath = localFullPath;
        var downloadedTempForThumb = false;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            var maxRemoteBytes = isImage ? 10L * 1024 * 1024 : 20L * 1024 * 1024;
            if (string.IsNullOrWhiteSpace(remoteFileId) || !_driveClient.IsConnected || sizeBytes > maxRemoteBytes)
            {
                return null;
            }

            var cachedSource = Path.Combine(_cacheDirectory, $"{remoteFileId}{ext}");
            if (!File.Exists(cachedSource))
            {
                await _driveClient.DownloadFileAsync(remoteFileId, cachedSource, null, cancellationToken);
                downloadedTempForThumb = sizeBytes > 2 * 1024 * 1024;
            }

            sourcePath = cachedSource;
        }

        try
        {
            if (isImage)
            {
                if (NativeBitmapExtensions.Contains(ext))
                {
                    await using var fs = File.OpenRead(sourcePath);
                    return Bitmap.DecodeToWidth(fs, 128);
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
                await using var fs = File.OpenRead(cachedThumb);
                return Bitmap.DecodeToWidth(fs, 128);
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
            psi.ArgumentList.Add($"TundraDrive — {title}");
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
                await using var fs = File.OpenRead(filePath);
                return Bitmap.DecodeToWidth(fs, 960);
            }

            var pngPath = Path.Combine(_cacheDirectory, $"{key}_img.png");
            if (!File.Exists(pngPath))
            {
                await RunToolAsync("magick", [filePath, "-resize", "960x960>", pngPath], ct);
                if (!File.Exists(pngPath))
                {
                    await RunToolAsync("ffmpeg", ["-y", "-i", filePath, "-vf", "scale='min(960,iw)':-1", "-frames:v", "1", pngPath], ct);
                }
            }

            if (File.Exists(pngPath))
            {
                await using var fs = File.OpenRead(pngPath);
                return Bitmap.DecodeToWidth(fs, 960);
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
        if (!File.Exists(thumbPath))
        {
            await RunToolAsync("ffmpeg", ["-y", "-ss", "00:00:02", "-i", filePath, "-vframes", "1", "-vf", "scale=720:-1", thumbPath], ct);
            if (!File.Exists(thumbPath))
            {
                await RunToolAsync("ffmpeg", ["-y", "-ss", "00:00:00", "-i", filePath, "-vframes", "1", "-vf", "scale=720:-1", thumbPath], ct);
            }
        }

        if (File.Exists(thumbPath))
        {
            await using var fs = File.OpenRead(thumbPath);
            return Bitmap.DecodeToWidth(fs, 720);
        }

        return null;
    }

    private async Task<Bitmap?> GenerateAudioWaveformAsync(string filePath, string key, CancellationToken ct)
    {
        var wavePath = Path.Combine(_cacheDirectory, $"{key}_wave.png");
        if (!File.Exists(wavePath))
        {
            await RunToolAsync("ffmpeg",
            [
                "-y", "-i", filePath,
                "-filter_complex", "showwavespic=s=640x160:colors=#3B82F6|#60A5FA",
                "-frames:v", "1", wavePath
            ], ct);
        }

        if (File.Exists(wavePath))
        {
            await using var fs = File.OpenRead(wavePath);
            return Bitmap.DecodeToWidth(fs, 640);
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
                await using var fs = File.OpenRead(expectedPng);
                return Bitmap.DecodeToWidth(fs, 960);
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
            return ".pdf";
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

            if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                return ".txt";
            }
        }

        return ext;
    }

    private static string ComputeFileKey(string filePath) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(filePath)))
            .ToLowerInvariant()[..16];

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

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

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
        if (File.Exists(targetPdf))
        {
            return targetPdf;
        }

        var loProfile = Path.Combine(_cacheDirectory, "lo_profile");
        Directory.CreateDirectory(loProfile);

        await RunToolAsync("soffice",
        [
            "--headless",
            "--norestore",
            $"-env:UserInstallation=file://{loProfile}",
            "--convert-to", "pdf",
            "--outdir", outDir,
            filePath
        ], ct);

        var generatedName = Path.ChangeExtension(Path.GetFileName(filePath), ".pdf");
        var generatedPath = Path.Combine(outDir, generatedName);
        if (File.Exists(generatedPath))
        {
            if (!string.Equals(generatedPath, targetPdf, StringComparison.Ordinal))
            {
                File.Move(generatedPath, targetPdf, overwrite: true);
            }

            return targetPdf;
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

            var output = (await proc.StandardOutput.ReadToEndAsync(ct)).Trim();
            await proc.WaitForExitAsync(ct);

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
                await proc.WaitForExitAsync(ct);
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
