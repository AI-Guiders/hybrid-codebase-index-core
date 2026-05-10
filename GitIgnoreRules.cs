using Ignore;

namespace HybridCodebaseIndex.Core;

internal static class GitIgnoreRules
{
    internal static Ignore.Ignore TryLoad(string workspaceRoot, IReadOnlyList<string> ignoreFiles)
    {
        var ig = new Ignore.Ignore();

        // Baseline denylist already handled by WorkspaceScanner.ShouldExcludePath.
        // Here we only apply gitignore-like rules.
        foreach (var relPath0 in ignoreFiles)
        {
            var relPath = relPath0?.Trim();
            if (string.IsNullOrWhiteSpace(relPath))
                continue;

            // Avoid path traversal / absolute paths in configuration.
            if (Path.IsPathRooted(relPath) || relPath.Contains("..", StringComparison.Ordinal))
                continue;

            var parts = relPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                continue;

            var abs = Path.Combine([workspaceRoot, ..parts]);
            TryAddFileRules(ig, abs);
        }

        return ig;
    }

    internal static bool IsIgnored(Ignore.Ignore rules, string relativePathUnix)
        => rules.IsIgnored(relativePathUnix);

    private static void TryAddFileRules(Ignore.Ignore rules, string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            // The library accepts raw lines (comments/empties are handled by lib).
            var lines = File.ReadAllLines(path);
            rules.Add(lines);
        }
        catch
        {
            // best-effort; treat as "no extra rules"
        }
    }
}
