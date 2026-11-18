using CommunityToolkit.Mvvm.ComponentModel;
using TundraDrive.Core.Models;

namespace TundraDrive.Desktop.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Computes proportional pixel widths for Drive, Other (Gmail/Photos), and Trash quota segments.
    /// </summary>
    protected static (double DriveWidth, double OtherWidth, double TrashWidth) ComputeStorageBarWidths(
        StorageQuotaInfo quota,
        double trackWidth)
    {
        var otherBytes = Math.Max(0, quota.UsageBytes - quota.UsageInDriveBytes - quota.UsageInTrashBytes);
        var driveBytes = Math.Max(0, quota.UsageInDriveBytes);
        var trashBytes = Math.Max(0, quota.UsageInTrashBytes);
        var totalUsed = driveBytes + otherBytes + trashBytes;

        if (totalUsed <= 0 || trackWidth <= 0)
        {
            return (0, 0, 0);
        }

        var usedWidth = quota.LimitBytes > 0
            ? Math.Clamp(trackWidth * ((double)totalUsed / quota.LimitBytes), 24.0, trackWidth)
            : Math.Min(trackWidth, 96.0);

        double trashWidth = 0;
        if (trashBytes > 0)
        {
            var rawTrash = usedWidth * ((double)trashBytes / totalUsed);
            trashWidth = usedWidth >= 8.0 ? Math.Max(1.0, rawTrash) : rawTrash;
            trashWidth = Math.Min(trashWidth, usedWidth);
        }

        var remainingWidth = Math.Max(0, usedWidth - trashWidth);
        var activeBytes = driveBytes + otherBytes;
        if (activeBytes <= 0)
        {
            return (0, 0, trashWidth);
        }

        var driveWidth = remainingWidth * ((double)driveBytes / activeBytes);
        if (driveBytes > 0 && remainingWidth >= 6.0 && driveWidth < 2.0)
        {
            driveWidth = 2.0;
        }

        var otherWidth = Math.Max(0, remainingWidth - driveWidth);
        if (otherBytes > 0 && remainingWidth >= 6.0 && otherWidth < 2.0)
        {
            otherWidth = 2.0;
            driveWidth = Math.Max(0, remainingWidth - otherWidth);
        }

        return (driveWidth, otherWidth, trashWidth);
    }
}
