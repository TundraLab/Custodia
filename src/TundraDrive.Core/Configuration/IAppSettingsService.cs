using TundraDrive.Core.Models;

namespace TundraDrive.Core.Configuration;

public interface IAppSettingsService
{
    AppSettings Current { get; }
    event EventHandler<AppSettings>? SettingsChanged;
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task SaveSilentlyAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
