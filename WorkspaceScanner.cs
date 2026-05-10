namespace HybridCodebaseIndex.Core;

using System.Text;

internal static class WorkspaceScanner
{
    internal static IEnumerable<string> EnumerateIndexableFiles(
        string rootDirectory,
        IReadOnlySet<string> extensions,
        IReadOnlyList<string> excludePathSegments,
        IReadOnlyList<string> excludeRootFullPaths)
    {
        var normalizedRoot = Path.GetFullPath(rootDirectory.TrimEnd(Path.DirectorySeparatorChar));
        if (!Directory.Exists(normalizedRoot))
            yield break;

        // Single-pass traversal (directory stack) to avoid N passes per extension.
        var stack = new Stack<string>();
        stack.Push(normalizedRoot);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            if (ShouldExcludeDirectory(dir, excludePathSegments, excludeRootFullPaths))
                continue;

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sd in subDirs)
                stack.Push(sd);

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var f in files)
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext.Length == 0)
                    continue;
                if (extensions.Contains(ext))
                    yield return f;
            }
        }
    }

    internal static bool ShouldExcludePath(string fullPath, IReadOnlyList<string> excludePathSegments)
    {
        if (excludePathSegments.Count == 0)
            return false;

        // Normalize for segment matching.
        // Use directory separators to avoid accidental substring matches.
        foreach (var seg0 in excludePathSegments)
        {
            var seg = seg0?.Trim();
            if (string.IsNullOrEmpty(seg))
                continue;

            var token = $"{Path.DirectorySeparatorChar}{seg}{Path.DirectorySeparatorChar}";
            if (fullPath.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ShouldExcludeDirectory(
        string fullPath,
        IReadOnlyList<string> excludePathSegments,
        IReadOnlyList<string> excludeRootFullPaths)
    {
        foreach (var root in excludeRootFullPaths)
        {
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (excludePathSegments.Count == 0)
            return false;

        // Quick check by directory name first.
        var name = Path.GetFileName(fullPath);
        if (!string.IsNullOrEmpty(name))
        {
            foreach (var seg0 in excludePathSegments)
            {
                if (string.Equals(name, seg0?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    internal static bool LooksBinary(ReadOnlySpan<byte> probe)
    {
        foreach (var b in probe)
        {
            if (b == 0)
                return true;
        }

        return false;
    }

    internal static string RelativePath(string workspaceRoot, string filePath)
    {
        workspaceRoot = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        filePath = Path.GetFullPath(filePath);
        return Path.GetRelativePath(workspaceRoot, filePath);
    }

    internal static IEnumerable<(int lineStart, int lineEnd, string body)> ChunkByLines(
        string text,
        int chunkLines,
        int overlapLines)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        // Normalize line endings so line accounting is stable.
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length == 0)
            yield break;

        var chunk = Math.Max(20, chunkLines);
        var overlap = Math.Clamp(overlapLines, 0, chunk - 1);
        var step = Math.Max(1, chunk - overlap);

        for (var i = 0; i < lines.Length; i += step)
        {
            var endExclusive = Math.Min(lines.Length, i + chunk);
            var sb = new StringBuilder(capacity: 4096);
            for (var j = i; j < endExclusive; j++)
            {
                if (j > i) sb.Append('\n');
                sb.Append(lines[j]);
            }

            // 1-based inclusive line numbers.
            yield return (i + 1, endExclusive, sb.ToString());

            if (endExclusive == lines.Length)
                yield break;
        }
    }
}
