using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using HybridCodebaseIndex.Core.Embeddings;
using Microsoft.Data.Sqlite;

namespace HybridCodebaseIndex.Core;

internal static partial class SqliteFtsIndex
{
    internal static Task<(SearchResponse response, string? error)> SearchHybridAsync(
        string workspaceRoot,
        string dbPath,
        string query,
        int topN,
        string? pathPrefix,
        IReadOnlyList<string>? excludePathPrefixes,
        IReadOnlyList<string>? extensions,
        bool semantic,
        double alpha,
        double beta,
        int vecTopK,
        CancellationToken cancellationToken)
        => Task.Run(() => SearchHybrid(workspaceRoot, dbPath, query, topN, pathPrefix, excludePathPrefixes, extensions, semantic, alpha, beta, vecTopK, cancellationToken), cancellationToken);

    private static (SearchResponse response, string? error) SearchHybrid(
        string workspaceRoot,
        string dbPath,
        string userQuery,
        int topN,
        string? pathPrefix,
        IReadOnlyList<string>? excludePathPrefixes,
        IReadOnlyList<string>? extensions,
        bool semantic,
        double alpha,
        double beta,
        int vecTopK,
        CancellationToken cancellationToken)
    {
        // FTS always available
        var (ftsResp, ftsErr) = Search(workspaceRoot, dbPath, userQuery, topN, pathPrefix, excludePathPrefixes, extensions);
        if (!semantic)
            return (ftsResp, ftsErr);
        if (!string.IsNullOrEmpty(ftsErr))
            return (ftsResp, ftsErr);

        if (!File.Exists(dbPath))
            return (ftsResp, ftsErr);

        workspaceRoot = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        // If vec backend missing, just return FTS.
        if (!TableExists(conn, "vectors") && !TableExists(conn, "vec_chunks"))
            return (ftsResp, null);

        var settings = IndexSettings.TryLoadFromIndexDirectory(Path.GetDirectoryName(dbPath)!);
        if (!settings.SemanticEnabled)
            return (ftsResp, null);

        var provider = EmbeddingProviderFactory.Create(settings, Path.GetDirectoryName(dbPath));
        var qv = provider.EmbedAsync(userQuery, cancellationToken).GetAwaiter().GetResult();
        var qn = Norm(qv);
        if (qn <= 1e-12)
            return (ftsResp, null);

        vecTopK = Math.Clamp(vecTopK, 5, 200);

        var indexDir = Path.GetDirectoryName(dbPath)!;
        var sqliteVecLoaded = SqliteVecInterop.TryEnableAndLoad(conn, settings, indexDir, out _);
        List<(long chunkRowId, double sim)> vecHitsRaw =
            sqliteVecLoaded && TableExists(conn, "vec_chunks")
                ? SqliteVecInterop.QueryTopK(conn, qv, vecTopK)
                : TableExists(conn, "vectors")
                    ? VecTopK(conn, qv, qn, vecTopK, settings)
                    : [];
        var vecHits = FilterVecHitsByAllowedChunkExtensions(conn, settings, vecHitsRaw);

        // Merge by hitId (chunk rowid)
        var merged = new Dictionary<long, IndexHit>();
        foreach (var h in ftsResp.Hits)
            merged[h.HitId] = h;

        foreach (var (chunkId, sim) in vecHits)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (merged.TryGetValue(chunkId, out var existing))
            {
                var fused = new IndexHit(
                    existing.HitId,
                    existing.Path,
                    existing.Extension,
                    existing.HitKind,
                    RankScore: alpha * (existing.FtsScore ?? existing.RankScore) + beta * sim,
                    FtsScore: existing.FtsScore ?? existing.RankScore,
                    VecScore: sim,
                    existing.Snippet,
                    existing.LineStart,
                    existing.LineEnd,
                    existing.ChunkCharCount,
                    existing.LastWriteUtcIso);
                merged[chunkId] = fused;
                continue;
            }

            // Fetch minimal chunk metadata for vec-only hits.
            if (!TryGetChunk(conn, chunkId, out var hit))
                continue;

            merged[chunkId] = new IndexHit(
                hit.HitId,
                hit.Path,
                hit.Extension,
                HitKinds.TextVector,
                RankScore: sim,
                FtsScore: null,
                VecScore: sim,
                hit.Snippet,
                hit.LineStart,
                hit.LineEnd,
                hit.ChunkCharCount,
                hit.LastWriteUtcIso);
        }

        var ordered = merged.Values
            .OrderByDescending(static h => h.RankScore)
            .Take(topN)
            .ToList();

        return (new SearchResponse(FormatVersion, userQuery, dbPath, ordered), null);
    }

    /// <summary>Вектор не должен «протекать» для расширений вне FTS-правил до следующего vec_reindex.</summary>
    private static List<(long chunkRowId, double sim)> FilterVecHitsByAllowedChunkExtensions(
        SqliteConnection conn,
        IndexSettings settings,
        List<(long chunkRowId, double sim)> hits)
    {
        if (hits.Count == 0)
            return hits;

        var allowed = settings.GetEffectiveVecExtensionsSet();
        if (allowed.Count == 0)
            return [];

        var distinctIds = hits.Select(static h => h.chunkRowId).Distinct().ToArray();
        using var cmd = conn.CreateCommand();
        var sb = new StringBuilder("SELECT rowid, extension FROM chunks WHERE rowid IN (");
        for (var i = 0; i < distinctIds.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            var p = "$i" + i.ToString(CultureInfo.InvariantCulture);
            sb.Append(p);
            cmd.Parameters.AddWithValue(p, distinctIds[i]);
        }

        sb.Append(')');

        cmd.CommandText = sb.ToString();

        var extOf = new Dictionary<long, string>(distinctIds.Length);
        using (var rr = cmd.ExecuteReader())
        {
            while (rr.Read())
            {
                var id = rr.GetInt64(0);
                var ext = rr.IsDBNull(1) ? "" : rr.GetString(1);
                extOf[id] = ext;
            }
        }

        return hits.Where(h =>
            extOf.TryGetValue(h.chunkRowId, out var ext) && allowed.Contains(ext)).ToList();
    }

    private static bool TableExists(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    private static List<(long chunkRowId, double sim)> VecTopK(SqliteConnection conn, float[] qv, double qn, int k, IndexSettings settings)
    {
        var allowed = settings.GetEffectiveVecExtensionsSet();
        if (allowed.Count == 0)
            return [];

        using var cmd = conn.CreateCommand();
        var sb = new StringBuilder(
            """
            SELECT v.chunk_rowid, v.dim, v.norm, v.vec FROM vectors v
            INNER JOIN chunks c ON c.rowid = v.chunk_rowid
            WHERE lower(c.extension) IN (
            """);

        var allowedList = allowed.Select(static e => e.ToLowerInvariant()).OrderBy(static e => e, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < allowedList.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            var p = "$e" + i.ToString(CultureInfo.InvariantCulture);
            sb.Append(p);
            cmd.Parameters.AddWithValue(p, allowedList[i]);
        }

        sb.Append(");");
        cmd.CommandText = sb.ToString();

        var top = new List<(long id, double sim)>(capacity: k);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            var dim = r.GetInt32(1);
            var norm = r.GetDouble(2);
            if (dim != qv.Length || norm <= 1e-12)
                continue;
            var blob = (byte[])r.GetValue(3);
            var sim = DotBlob(blob, qv) / (qn * norm);
            InsertTopK(top, (id, sim), k);
        }

        return top.Select(static x => (x.id, x.sim)).ToList();
    }

    private static void InsertTopK(List<(long id, double sim)> top, (long id, double sim) item, int k)
    {
        if (top.Count < k)
        {
            top.Add(item);
            top.Sort(static (a, b) => b.sim.CompareTo(a.sim));
            return;
        }

        if (item.sim <= top[^1].sim)
            return;

        top[^1] = item;
        top.Sort(static (a, b) => b.sim.CompareTo(a.sim));
    }

    private static double DotBlob(byte[] blob, float[] qv)
    {
        double sum = 0;
        for (var i = 0; i < qv.Length; i++)
        {
            var f = BinaryPrimitives.ReadSingleLittleEndian(blob.AsSpan(i * 4, 4));
            sum += f * qv[i];
        }
        return sum;
    }

    private static double Norm(float[] v)
    {
        double sum = 0;
        foreach (var x in v)
            sum += x * x;
        return Math.Sqrt(sum);
    }

    private static bool TryGetChunk(SqliteConnection conn, long id, out IndexHit hit)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.rowid, c.path, c.extension, c.line_start, c.line_end, length(c.body), snippet(chunks, 4, '[', ']', ' … ', 24), fs.last_write_utc_ticks
            FROM chunks c
            LEFT JOIN file_state fs ON fs.path = c.path
            WHERE c.rowid = $id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            hit = null!;
            return false;
        }

        var path = r.GetString(1);
        var ext = r.IsDBNull(2) ? "" : r.GetString(2);
        var ls = r.IsDBNull(3) ? 0 : r.GetInt32(3);
        var le = r.IsDBNull(4) ? 0 : r.GetInt32(4);
        var chars = r.IsDBNull(5) ? 0 : r.GetInt32(5);
        var snip = r.IsDBNull(6) ? null : r.GetString(6);
        var lastWriteIso = r.IsDBNull(7) ? null : new DateTime(r.GetInt64(7), DateTimeKind.Utc).ToString("O");

        hit = new IndexHit(id, path, ext, HitKinds.TextVector, 0, FtsScore: null, VecScore: null, snip, ls, le, chars, lastWriteIso);
        return true;
    }
}

