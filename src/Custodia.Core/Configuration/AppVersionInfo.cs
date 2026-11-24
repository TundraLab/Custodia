using System.Reflection;

namespace TundraDrive.Core.Configuration;

public static class AppVersionInfo
{
    public const string FallbackVersion = "3.0.1";
    public const string RepositoryOwner = "TundraLab";
    public const string RepositoryName = "Custodia";
    public const string CurrentLegalVersion = "2026-11-24";

    public static string CurrentVersion { get; } = ResolveCurrentVersion();

    public static string ResolveCurrentVersion(Assembly? assembly = null)
    {
        var target = assembly ?? Assembly.GetEntryAssembly() ?? typeof(AppVersionInfo).Assembly;
        var informational = target
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var cleaned = informational.Trim().TrimStart('v', 'V');
            var plusIndex = cleaned.IndexOf('+');
            if (plusIndex > 0)
            {
                cleaned = cleaned[..plusIndex];
            }

            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }
        }

        var assemblyVersion = target.GetName().Version;
        if (assemblyVersion is not null)
        {
            var build = assemblyVersion.Build >= 0 ? assemblyVersion.Build : 0;
            return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{build}";
        }

        return FallbackVersion;
    }

    public static bool TryParseSemVer(string? rawVersion, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var cleaned = rawVersion.Trim().TrimStart('v', 'V');
        var separatorIdx = cleaned.IndexOfAny(['-', '+']);
        if (separatorIdx > 0)
        {
            cleaned = cleaned[..separatorIdx];
        }

        if (!Version.TryParse(cleaned, out var parsed))
        {
            return false;
        }

        var major = Math.Max(0, parsed.Major);
        var minor = Math.Max(0, parsed.Minor);
        var build = Math.Max(0, parsed.Build);
        var revision = Math.Max(0, parsed.Revision);
        version = new Version(major, minor, build, revision);
        return true;
    }

    public static bool IsNewerVersion(string? candidateVersion, string? currentVersion = null)
    {
        var effectiveCurrent = string.IsNullOrWhiteSpace(currentVersion) ? CurrentVersion : currentVersion;
        if (!TryParseSemVer(candidateVersion, out var candidateParsed) ||
            !TryParseSemVer(effectiveCurrent, out var currentParsed))
        {
            return false;
        }

        return candidateParsed > currentParsed;
    }
}
