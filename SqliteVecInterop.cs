using System.Buffers.Binary;
using Microsoft.Data.Sqlite;

namespace HybridCodebaseIndex.Core;

internal static class SqliteVecInterop
{
    internal static bool TryEnableAndLoad(SqliteConnection conn, IndexSettings settings, string indexDirectory, out string? error)
    {
        error = null;

        var raw = (settings.SqliteVecExtensionPath ?? "").Trim();
        if (raw.Length == 0)
            return false;

        var extPath = ResolveExtensionPath(raw, indexDirectory);
        if (extPath is null)
            return false;

        try
        {
            conn.EnableExtensions(true);
            conn.LoadExtension(extPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
        finally
        {
            try { conn.EnableExtensions(false); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Absolute path as-is if it exists; otherwise relative to index directory, then to <see cref="AppContext.BaseDirectory"/> (published MCP host).
    /// </summary>
    private static string? ResolveExtensionPath(string raw, string indexDirectory)
    {
        if (Path.IsPathRooted(raw))
            return File.Exists(raw) ? raw : null;

        var inIndex = Path.Combine(indexDirectory, raw);
        if (File.Exists(inIndex))
            return inIndex;

        var inBase = Path.Combine(AppContext.BaseDirectory, raw);
        return File.Exists(inBase) ? inBase : null;
    }

    internal static bool TryEnsureVecChunksTable(SqliteConnection conn, int dim, out string? error)
    {
        error = null;
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE VIRTUAL TABLE IF NOT EXISTS vec_chunks USING vec0(embedding float[{dim}] distance_metric=cosine);";
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    internal static bool TryUpsertVector(SqliteConnection conn, long chunkRowId, float[] v, out string? error)
    {
        error = null;
        try
        {
            var blob = SerializeF32(v);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO vec_chunks(rowid, embedding) VALUES($id, $emb);";
            cmd.Parameters.AddWithValue("$id", chunkRowId);
            cmd.Parameters.AddWithValue("$emb", blob);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    internal static List<(long chunkRowId, double sim)> QueryTopK(SqliteConnection conn, float[] qv, int k)
    {
        // cosine distance: 0 = identical, 1 = orthogonal, 2 = opposite (depending on implementation)
        // Similarity proxy for fusion: sim = 1 - distance, clamped.
        var blob = SerializeF32(qv);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT rowid, distance
            FROM vec_chunks
            WHERE embedding MATCH $q
            ORDER BY distance
            LIMIT $k;
            """;
        cmd.Parameters.AddWithValue("$q", blob);
        cmd.Parameters.AddWithValue("$k", k);

        var list = new List<(long chunkRowId, double sim)>(capacity: k);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            var dist = r.GetDouble(1);
            var sim = Math.Clamp(1.0 - dist, -1.0, 1.0);
            list.Add((id, sim));
        }
        return list;
    }

    private static byte[] SerializeF32(float[] v)
    {
        var blob = new byte[v.Length * 4];
        for (var i = 0; i < v.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(blob.AsSpan(i * 4, 4), v[i]);
        return blob;
    }
}

