namespace TundraDrive.Core.Configuration;

public static class AppPaths
{
    private static readonly string UserHome =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    static AppPaths()
    {
        TryMigrateLegacyDirectories();
    }

    public static string ConfigDirectory =>
        Path.Combine(UserHome, ".config", "custodia");

    public static string CredentialsFilePath =>
        Path.Combine(ConfigDirectory, "credentials.json");

    public static string SettingsFilePath =>
        Path.Combine(ConfigDirectory, "settings.json");

    public static string DataDirectory =>
        Path.Combine(UserHome, ".local", "share", "custodia");

    public static string TokensDirectory =>
        Path.Combine(DataDirectory, "tokens");

    public static string DatabaseFilePath =>
        Path.Combine(DataDirectory, "custodia.db");

    public static string UpdatesDirectory =>
        Path.Combine(DataDirectory, "updates");

    public static string DefaultDownloadDirectory =>
        Path.Combine(UserHome, "Downloads");

    private static void TryMigrateLegacyDirectories()
    {
        try
        {
            var legacyConfig = Path.Combine(UserHome, ".config", "tundradrive");
            var newConfig = Path.Combine(UserHome, ".config", "custodia");
            if (Directory.Exists(legacyConfig) && !Directory.Exists(newConfig))
            {
                CopyDirectoryRecursive(legacyConfig, newConfig);
            }

            var legacyData = Path.Combine(UserHome, ".local", "share", "tundradrive");
            var newData = Path.Combine(UserHome, ".local", "share", "custodia");
            if (Directory.Exists(legacyData) && !Directory.Exists(newData))
            {
                CopyDirectoryRecursive(legacyData, newData);
                var migratedOldDb = Path.Combine(newData, "tundradrive.db");
                var targetNewDb = Path.Combine(newData, "custodia.db");
                if (File.Exists(migratedOldDb) && !File.Exists(targetNewDb))
                {
                    File.Move(migratedOldDb, targetNewDb);
                }
            }
        }
        catch
        {
            // Non-fatal migration from previous TundraDrive paths.
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "tundradrive.lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destFile = Path.Combine(targetDir, name);
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(subDir);
            if (string.Equals(name, "updates", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectoryRecursive(subDir, Path.Combine(targetDir, name));
        }
    }
}
