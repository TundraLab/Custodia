namespace TundraDrive.Core.Models;

public sealed class StorageQuotaInfo
{
    public long LimitBytes { get; set; }
    public long UsageBytes { get; set; }
    public long UsageInDriveBytes { get; set; }
    public long UsageInTrashBytes { get; set; }

    public double UsagePercentage =>
        LimitBytes <= 0 ? 0 : Math.Clamp((double)UsageBytes / LimitBytes * 100.0, 0, 100);
}

public sealed class DriveAccount
{
    public string Id { get; set; } = "default";
    public string DisplayName { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public bool IsConnected { get; set; }
    public DateTimeOffset? ConnectedAtUtc { get; set; }
    public StorageQuotaInfo Quota { get; set; } = new();
}
