namespace TundraDrive.Core.Models;

public sealed record FolderAppearanceOverride(
    string? Alias = null,
    string? ColorHex = null,
    string? IconKey = null);

public sealed class AppSettings
{
    public string Language { get; set; } = "pt-BR";
    public string UiAccent { get; set; } = "blue";
    public string ActiveAccountId { get; set; } = "default";
    public int UiScalePercent { get; set; } = 100;
    public bool StartOnBoot { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public bool NotificationSoundEnabled { get; set; } = true;
    public string NotificationSoundTone { get; set; } = "chime";
    public bool GmailAccountAlertsEnabled { get; set; } = true;
    public string MailerWebhookUrl { get; set; } = string.Empty;
    public string? AcceptedLegalVersion { get; set; }
    public Dictionary<string, FolderAppearanceOverride> FolderAppearances { get; set; } = new(StringComparer.Ordinal);
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public string DownloadDirectory { get; set; } = Configuration.AppPaths.DefaultDownloadDirectory;
    public int SyncIntervalSeconds { get; set; } = 120;
    public bool WatchLocalChanges { get; set; } = true;
    public bool EmbeddedApiEnabled { get; set; } = true;
    public int EmbeddedApiPort { get; set; } = 5080;
    public bool AutoMemoryCleanupEnabled { get; set; } = true;
    public int MemoryCleanupIntervalMinutes { get; set; } = 5;
    public bool AutoCheckUpdates { get; set; } = true;
    public int UpdateCheckIntervalDays { get; set; } = 30;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public ConflictResolutionPolicy ConflictPolicy { get; set; } = ConflictResolutionPolicy.KeepNewest;
    public List<string> IgnorePatterns { get; set; } =
    [
        "*.tmp",
        "*.swp",
        "*.part",
        "*.crdownload",
        ".DS_Store",
        "Thumbs.db",
        ".goutputstream-*",
        ".git/*",
        "node_modules/*",
        "bin/*",
        "obj/*"
    ];
}
