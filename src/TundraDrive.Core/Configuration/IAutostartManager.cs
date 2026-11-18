namespace TundraDrive.Core.Configuration;

public interface IAutostartManager
{
    string DesktopEntryPath { get; }
    bool IsSupported { get; }
    bool IsEnabled();
    void SetEnabled(bool enabled);
}
