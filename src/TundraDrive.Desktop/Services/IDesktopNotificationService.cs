namespace TundraDrive.Desktop.Services;

public enum NotificationUrgency
{
    Low = 0,
    Normal = 1,
    Critical = 2
}

public interface IDesktopNotificationService
{
    Task NotifyAsync(
        string title,
        string body,
        NotificationUrgency urgency = NotificationUrgency.Normal,
        bool bypassThrottle = false,
        string? targetPath = null);
}
