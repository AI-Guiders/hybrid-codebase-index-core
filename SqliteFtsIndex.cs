using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HybridCodebaseIndex.Core;

internal static partial class SqliteFtsIndex
{
    // Bump when on-disk schema changes in a non-backward-compatible way.
    internal const int FormatVersion = 2;

    private static void InitEmptyIndex(SqliteConnection conn, string workspaceRoot)
    {
        void Exec(string sql)
        {
            using var c = conn.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE meta(
              key TEXT PRIMARY KEY,
              value TEXT
            );
            """);
        Exec("""
            CREATE VIRTUAL TABLE chunks USING fts5(
              path,
              extension UNINDEXED,
              line_start UNINDEXED,
              line_end UNINDEXED,
              body,
              tokenize='unicode61 remove_diacritics 1'
            );
            """);

        using (var ins = conn.CreateCommand())
        {
            ins.CommandText = """
                INSERT INTO meta(key,value) VALUES('format_version', $v);
                """;
            ins.Parameters.AddWithValue("$v", FormatVersion.ToString(CultureInfo.InvariantCulture));
            ins.ExecuteNonQuery();
        }

        using (var ins = conn.CreateCommand())
        {
            ins.CommandText = """
                INSERT INTO meta(key,value) VALUES('indexed_at', $ts);
                """;
            ins.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            ins.ExecuteNonQuery();
        }

        using (var ins = conn.CreateCommand())
        {
            ins.CommandText = """
                INSERT INTO meta(key,value) VALUES('workspace_root', $wr);
                """;
            ins.Parameters.AddWithValue("$wr", workspaceRoot);
            ins.ExecuteNonQuery();
        }
    }

    // (moved) SearchAsync/Search/ExplainHitAsync/ExplainHit/BuildMatchQuery live in SqliteFtsIndex.Query.cs
}
