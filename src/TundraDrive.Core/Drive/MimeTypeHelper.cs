namespace TundraDrive.Core.Drive;

public static class MimeTypeHelper
{
    public const string GoogleFolderMimeType = "application/vnd.google-apps.folder";
    private const string GoogleWorkspacePrefix = "application/vnd.google-apps.";

    private static readonly Dictionary<string, string> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".yaml"] = "application/yaml",
        [".yml"] = "application/yaml",
        [".csv"] = "text/csv",
        [".html"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".ts"] = "application/typescript",
        [".cs"] = "text/plain",
        [".py"] = "text/x-python",
        [".sh"] = "application/x-sh",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".gif"] = "image/gif",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".mp4"] = "video/mp4",
        [".mkv"] = "video/x-matroska",
        [".zip"] = "application/zip",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".ods"] = "application/vnd.oasis.opendocument.spreadsheet"
    };

    public static bool IsGoogleFolder(string? mimeType) =>
        string.Equals(mimeType, GoogleFolderMimeType, StringComparison.OrdinalIgnoreCase);

    public static bool IsGoogleWorkspaceDocument(string? mimeType) =>
        !string.IsNullOrWhiteSpace(mimeType) &&
        mimeType.StartsWith(GoogleWorkspacePrefix, StringComparison.OrdinalIgnoreCase) &&
        !IsGoogleFolder(mimeType);

    public static string GetMimeTypeFromFilePath(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext) && ExtensionMap.TryGetValue(ext, out var mimeType))
        {
            return mimeType;
        }

        return "application/octet-stream";
    }
}
