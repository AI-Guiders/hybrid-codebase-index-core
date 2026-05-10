using System.Text;
using Microsoft.Data.Sqlite;

namespace HybridCodebaseIndex.Core;

internal static partial class SqliteFtsIndex
{
    internal static Task<DocDraftResponse> DraftDocAsync(
        string workspaceRoot,
        string dbPath,
        string title,
        IReadOnlyList<string> changedPaths,
        CancellationToken cancellationToken)
        => Task.Run(() => DraftDoc(workspaceRoot, dbPath, title, changedPaths), cancellationToken);

    private static DocDraftResponse DraftDoc(
        string workspaceRoot,
        string dbPath,
        string title,
        IReadOnlyList<string> changedPaths)
    {
        workspaceRoot = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        if (!File.Exists(dbPath))
            return new DocDraftResponse(FormatVersion, dbPath, title, "", "Index database not found; run codebase_index_reindex.");

        if (changedPaths.Count == 0)
            return new DocDraftResponse(FormatVersion, dbPath, title, "", "changed_paths is empty.");

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        var sb = new StringBuilder(capacity: 4096);
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine("- TODO");
        sb.AppendLine();
        sb.AppendLine("## Motivation");
        sb.AppendLine("- TODO");
        sb.AppendLine();
        sb.AppendLine("## Changes");
        sb.AppendLine();

        foreach (var raw in changedPaths.Take(200))
        {
            var p = raw.Trim().Replace("\\", "/", StringComparison.Ordinal).TrimStart('/');
            if (p.Length == 0)
                continue;

            sb.AppendLine($"### `{p}`");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT rowid, extension, line_start, line_end, substr(body, 1, 900)
                FROM chunks
                WHERE path = $p
                ORDER BY line_start ASC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$p", p);

            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                sb.AppendLine("- (no indexed content found for this path)");
                sb.AppendLine();
                continue;
            }

            var ext = r.IsDBNull(1) ? "" : r.GetString(1);
            var ls = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            var le = r.IsDBNull(3) ? 0 : r.GetInt32(3);
            var excerpt = r.IsDBNull(4) ? "" : r.GetString(4);

            sb.AppendLine($"- extension: `{ext}`");
            sb.AppendLine($"- lines: {ls}..{le}");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(excerpt.TrimEnd());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## Notes / Follow-ups");
        sb.AppendLine("- TODO");
        sb.AppendLine();

        return new DocDraftResponse(FormatVersion, dbPath, title, sb.ToString(), null);
    }
}

