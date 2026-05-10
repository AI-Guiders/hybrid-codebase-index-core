using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace HybridCodebaseIndex.Core;

/// <summary>
/// Embedded copies of shipped files (see <c>EmbeddedResource</c> in csproj).
/// Resource name: <c>HybridCodebaseIndex.Core.</c> + relative path with <c>/</c> replaced by <c>.</c>.
/// </summary>
internal static class BundledContent
{
    private static readonly Assembly s_assembly = typeof(BundledContent).Assembly;
    private const string ResourcePrefix = "HybridCodebaseIndex.Core.";

    internal static bool TryReadEmbeddedText(string relativePath, [NotNullWhen(true)] out string? text)
    {
        text = null;
        var normalized = NormalizeRelative(relativePath);
        if (normalized.Length == 0)
            return false;

        var name = ResourcePrefix + normalized.Replace('/', '.');
        using var stream = s_assembly.GetManifestResourceStream(name);
        if (stream is null)
            return false;
        using var reader = new StreamReader(stream);
        text = reader.ReadToEnd();
        return !string.IsNullOrWhiteSpace(text);
    }

    private static string NormalizeRelative(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/').Trim();
}

