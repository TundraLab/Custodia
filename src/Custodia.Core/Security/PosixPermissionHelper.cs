namespace TundraDrive.Core.Security;

public static class PosixPermissionHelper
{
    private const UnixFileMode Directory0700 =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode File0600 =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void EnsurePrivateDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(directoryPath, Directory0700);
        }
    }

    public static void EnsureFileMode0600(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(filePath, File0600);
        }
    }

    public static void EnforcePrivateDirectoryFiles0600(string directoryPath)
    {
        EnsurePrivateDirectory(directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            EnsureFileMode0600(file);
        }
    }

    public static string? GetOctalPermissions(string path)
    {
        if ((!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) ||
            (!File.Exists(path) && !Directory.Exists(path)))
        {
            return null;
        }

        var mode = File.GetUnixFileMode(path);
        var numeric = ((int)mode) & 0x1FF;
        return Convert.ToString(numeric, 8).PadLeft(4, '0');
    }
}
