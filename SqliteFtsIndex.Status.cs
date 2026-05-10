using Microsoft.Data.Sqlite;

namespace HybridCodebaseIndex.Core;

internal static partial class SqliteFtsIndex
{
    internal static Task<IndexStatus> GetStatusAsync(string workspaceRoot, string dbPath, CancellationToken cancellationToken)
        => Task.Run(() => GetStatus(workspaceRoot, dbPath), cancellationToken);

    private static IndexStatus GetStatus(string workspaceRoot, string dbPath)
    {
        workspaceRoot = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var exists = File.Exists(dbPath);
        if (!exists)
        {
            IndexSettings.TryLoadFromIndexDirectoryWithDiagnostics(
                Path.GetDirectoryName(dbPath),
                out _,
                out var src,
                out var err);
            return new IndexStatus(FormatVersion, dbPath, false, 0, false, null, workspaceRoot, null, null, src, err, null, null, null);
        }

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        var indexedAt = ReadMeta(conn, "indexed_at");
        var reindexState = ReadMeta(conn, "reindex_state");
        if (string.IsNullOrWhiteSpace(reindexState))
            reindexState = null;
        var reindexStartedAt = ReadMeta(conn, "reindex_started_at");
        if (string.IsNullOrWhiteSpace(reindexStartedAt))
            reindexStartedAt = null;
        var lastErr = ReadMeta(conn, "reindex_error");
        if (string.IsNullOrWhiteSpace(lastErr))
            lastErr = null;
        var lastErrAt = ReadMeta(conn, "reindex_error_at");
        if (string.IsNullOrWhiteSpace(lastErrAt))
            lastErrAt = null;

        IndexSettings.TryLoadFromIndexDirectoryWithDiagnostics(
            Path.GetDirectoryName(dbPath),
            out var settings,
            out var settingsSource,
            out var settingsParseError);

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT count(*) FROM chunks;";
        var docCount = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        var mayBeStale = string.Equals(reindexState, "running", StringComparison.OrdinalIgnoreCase);

        var eff = new EffectiveSettings(
            settings.IncludeCsInFts,
            settings.ExtraIncludeRoots,
            settings.ExcludeRoots,
            settings.GetEffectiveExtensions(),
            settings.ExcludePathSegments,
            settings.IgnoreFiles,
            settings.GetEffectiveMaxIndexedFileBytes(),
            settings.GetEffectiveChunkLines(),
            settings.GetEffectiveChunkOverlapLines(),
            settings.GetEffectiveBinaryProbeBytes());
        return new IndexStatus(
            FormatVersion,
            dbPath,
            true,
            docCount,
            mayBeStale,
            indexedAt,
            workspaceRoot,
            lastErr,
            lastErrAt,
            settingsSource,
            settingsParseError,
            eff,
            reindexState,
            reindexStartedAt);
    }
}

