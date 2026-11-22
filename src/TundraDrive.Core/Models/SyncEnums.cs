namespace TundraDrive.Core.Models;

public enum SyncMode
{
    TwoWay = 0,
    OneWayBackup = 1
}

public enum FolderSyncState
{
    Idle = 0,
    Syncing = 1,
    Paused = 2,
    Error = 3
}

public enum TransferDirection
{
    Upload = 0,
    Download = 1,
    Delete = 2
}

public enum TransferStatus
{
    Queued = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

public enum ActivitySeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public enum ConflictResolutionPolicy
{
    KeepNewest = 0,
    PreferLocal = 1,
    PreferRemote = 2,
    RenameConflictCopy = 3
}
