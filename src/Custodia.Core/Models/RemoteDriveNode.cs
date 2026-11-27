namespace TundraDrive.Core.Models;

public sealed class RemoteDriveNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public bool IsGoogleWorkspaceDoc { get; set; }
    public long SizeBytes { get; set; }
    public string? Md5Checksum { get; set; }
    public string? ThumbnailLink { get; set; }
    public string? WebViewLink { get; set; }
    public DateTimeOffset ModifiedTimeUtc { get; set; }
    public IReadOnlyList<string> ParentIds { get; set; } = Array.Empty<string>();
    public bool Trashed { get; set; }
    public bool Starred { get; set; }
}

public sealed class DrivePermissionEntry
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? EmailAddress { get; set; }
    public string? DisplayName { get; set; }
}
