namespace TundraDrive.Core.Configuration;

public static class AppPaths
{
    private static readonly string UserHome =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string ConfigDirectory =>
        Path.Combine(UserHome, ".config", "tundradrive");

    public static string CredentialsFilePath =>
        Path.Combine(ConfigDirectory, "credentials.json");

    public static string SettingsFilePath =>
        Path.Combine(ConfigDirectory, "settings.json");

    public static string DataDirectory =>
        Path.Combine(UserHome, ".local", "share", "tundradrive");

    public static string TokensDirectory =>
        Path.Combine(DataDirectory, "tokens");

    public static string DatabaseFilePath =>
        Path.Combine(DataDirectory, "tundradrive.db");

    public static string DefaultDownloadDirectory =>
        Path.Combine(UserHome, "Downloads");
}
