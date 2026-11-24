namespace TundraDrive.Core.Notifications;

public interface ITundraMailerClient
{
    Task<bool> TrySendLifecycleEmailAsync(
        string recipientEmail,
        string? recipientName,
        bool isConnected,
        string language = "pt-BR",
        CancellationToken cancellationToken = default);
}
