using System.Text;
using System.Text.RegularExpressions;

namespace HybridCodebaseIndex.Core;

/// <summary>
/// SoftFL densify: SQLite FTS5 <c>unicode61</c> keeps CamelCase/PascalCase as one token.
/// Expand identifiers at index time and split query terms so middle segments hit
/// (e.g. <c>BoardLeaf</c> finds <c>PlanBoardLeaf</c>).
/// </summary>
internal static partial class FtsCamelCase
{
    // Identifier-ish runs; snake_case already splits via unicode61 separators.
    [GeneratedRegex(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    /// <summary>Append CamelCase segment expansions so FTS indexes Plan/Board/Leaf separately.</summary>
    public static string ExpandBodyForFts(string body)
    {
        if (string.IsNullOrEmpty(body))
            return body;

        HashSet<string>? expansions = null;
        foreach (Match m in IdentifierRegex().Matches(body))
        {
            var ident = m.Value;
            var parts = SplitIdentifier(ident);
            if (parts.Count <= 1)
                continue;

            expansions ??= new HashSet<string>(StringComparer.Ordinal);
            expansions.Add(string.Join(' ', parts));
        }

        if (expansions is null || expansions.Count == 0)
            return body;

        var sb = new StringBuilder(body.Length + expansions.Count * 24 + 16);
        sb.Append(body);
        sb.Append("\n__hci_camel:\n");
        foreach (var line in expansions)
            sb.AppendLine(line);
        return sb.ToString();
    }

    /// <summary>FTS5 MATCH fragment for one user token (prefix + optional CamelCase AND).</summary>
    public static string BuildMatchTerm(string rawToken)
    {
        var safe = SanitizeToken(rawToken);
        if (safe.Length == 0)
            return "";

        var full = '"' + safe + "\"*";
        var parts = SplitIdentifier(safe);
        if (parts.Count <= 1)
            return full;

        var andParts = new List<string>(parts.Count);
        foreach (var p in parts)
        {
            var sp = SanitizeToken(p);
            if (sp.Length == 0)
                continue;
            andParts.Add('"' + sp + "\"*");
        }

        if (andParts.Count <= 1)
            return full;

        return "(" + full + " OR (" + string.Join(" AND ", andParts) + "))";
    }

    /// <summary>
    /// Split CamelCase / PascalCase / acronyms: PlanBoardLeaf → Plan,Board,Leaf;
    /// XMLHttpRequest → XML,Http,Request; getHTTPResponse → get,HTTP,Response.
    /// </summary>
    public static IReadOnlyList<string> SplitIdentifier(string ident)
    {
        if (string.IsNullOrEmpty(ident))
            return Array.Empty<string>();

        // Underscores: emit non-empty segments (unicode61 already separates; keep for query tokens).
        if (ident.Contains('_', StringComparison.Ordinal))
        {
            var snake = ident.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (snake.Length > 1)
                return snake;
        }

        var chars = ident.AsSpan();
        var parts = new List<string>(4);
        var start = 0;
        for (var i = 1; i < chars.Length; i++)
        {
            var prev = chars[i - 1];
            var cur = chars[i];
            var next = i + 1 < chars.Length ? chars[i + 1] : '\0';

            var boundary =
                (char.IsLower(prev) && char.IsUpper(cur))
                || (char.IsDigit(prev) != char.IsDigit(cur) && (char.IsLetter(prev) || char.IsLetter(cur)))
                || (char.IsUpper(prev) && char.IsUpper(cur) && char.IsLower(next));

            if (!boundary)
                continue;

            if (i > start)
                parts.Add(ident[start..i]);
            start = i;
        }

        if (start < chars.Length)
            parts.Add(ident[start..]);

        return parts.Count == 0 ? new[] { ident } : parts;
    }

    private static string SanitizeToken(string t) =>
        t.Replace("\"", "", StringComparison.Ordinal).Replace("'", "", StringComparison.Ordinal);
}
