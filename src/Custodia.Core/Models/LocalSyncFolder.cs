namespace TundraDrive.Core.Models;

public sealed class LocalSyncFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = "default";
    public string? AccountEmail { get; set; }
    public string LocalPath { get; set; } = string.Empty;
    public string RemoteFolderId { get; set; } = "root";
    public string RemoteFolderName { get; set; } = "Meus Arquivos";
    public SyncMode Mode { get; set; } = SyncMode.TwoWay;
    public FolderSyncState State { get; set; } = FolderSyncState.Idle;
    public bool IsPaused { get; set; }
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public string? LastErrorMessage { get; set; }
    public int TrackedFileCount { get; set; }
    public string? CustomAlias { get; set; }
    public string? CustomColorHex { get; set; }
    public string? CustomIconKey { get; set; }

    public string EffectiveDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CustomAlias))
            {
                return CustomAlias.Trim();
            }

            var folderName = Path.GetFileName(LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return !string.IsNullOrWhiteSpace(folderName) ? folderName : RemoteFolderName;
        }
    }
}
