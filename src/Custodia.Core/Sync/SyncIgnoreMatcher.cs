using System.IO.Enumeration;

namespace TundraDrive.Core.Sync;

public static class SyncIgnoreMatcher
{
    public static bool IsIgnored(string relativePath, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var fileName = Path.GetFileName(normalized);

        if (fileName.Contains(".tundratmp.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawPattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(rawPattern))
            {
                continue;
            }

            var pattern = rawPattern.Trim().Replace('\\', '/');
            if (pattern.EndsWith("/*", StringComparison.Ordinal))
            {
                var dirName = pattern[..^2];
                if (segments.Any(s => string.Equals(s, dirName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            else if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true) ||
                     FileSystemName.MatchesSimpleExpression(pattern, normalized, ignoreCase: true))
            {
                return true;
            }
        }

        return false;
    }
}
