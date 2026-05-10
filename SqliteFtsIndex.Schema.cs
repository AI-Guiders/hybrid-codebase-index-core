using Microsoft.Data.Sqlite;

namespace HybridCodebaseIndex.Core;

internal static partial class SqliteFtsIndex
{
    private static void EnsureChunksTable(SqliteConnection conn)
    {
        try
        {
            Exec(conn, "SELECT 1 FROM chunks LIMIT 1;");
        }
        catch (SqliteException)
        {
            Exec(conn, """
                CREATE VIRTUAL TABLE IF NOT EXISTS chunks USING fts5(
                  path,
                  extension UNINDEXED,
                  line_start UNINDEXED,
                  line_end UNINDEXED,
                  body,
                  tokenize='unicode61 remove_diacritics 1'
                );
                """);
        }
    }

    private static void EnsureFileStateTable(SqliteConnection conn)
    {
        Exec(conn, """
            CREATE TABLE IF NOT EXISTS file_state(
              path TEXT PRIMARY KEY,
              size_bytes INTEGER NOT NULL,
              last_write_utc_ticks INTEGER NOT NULL
            );
            """);
    }
}

