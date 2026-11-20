namespace TundraDrive.Core.Models;

public sealed class SyncActivityLog
{
    public long Id { get; set; }
    public string? SyncFolderId { get; set; }
    public ActivitySeverity Severity { get; set; } = ActivitySeverity.Info;
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
