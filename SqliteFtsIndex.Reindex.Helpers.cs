using System.Text;
using System.Text.RegularExpressions;
using HybridCodebaseIndex.Core.Indexing;
using Microsoft.Data.Sqlite;

namespace HybridCodebaseIndex.Core;

internal static partial class SqliteFtsIndex
{
    private static string BuildArtifactAugmentationHeader(
        string workspaceRootNormalized,
        string absolutePath,
        string relPathUnix,
        string ext,
        string text)
    {
        // Keep this cheap and best-effort: it must never break indexing.
        // The goal is to improve searchability for Razor/AXAML without adding a full parser.
        try
        {
            ext = ext.ToLowerInvariant();
            if (ext is not ".razor" and not ".axaml" and not ".cs")
                return "";

            var sb = new StringBuilder(capacity: 512);

            // Pairing: .razor <-> .razor.cs, .axaml <-> .axaml.cs
            if (ext is ".razor" or ".axaml")
            {
                var codeBehind = absolutePath + ".cs";
                if (File.Exists(codeBehind))
                {
                    var rel = WorkspaceScanner.RelativePath(workspaceRootNormalized, codeBehind).Replace("\\", "/", StringComparison.Ordinal);
                    sb.Append("__hci_pair:");
                    sb.Append(rel);
                    sb.AppendLine();
                }
            }
            else if (ext == ".cs")
            {
                if (absolutePath.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
                {
                    var razor = absolutePath[..^3]; // drop ".cs"
                    if (File.Exists(razor))
                    {
                        var rel = WorkspaceScanner.RelativePath(workspaceRootNormalized, razor).Replace("\\", "/", StringComparison.Ordinal);
                        sb.Append("__hci_pair:");
                        sb.Append(rel);
                        sb.AppendLine();
                    }
                }
                else if (absolutePath.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase))
                {
                    var axaml = absolutePath[..^3]; // drop ".cs"
                    if (File.Exists(axaml))
                    {
                        var rel = WorkspaceScanner.RelativePath(workspaceRootNormalized, axaml).Replace("\\", "/", StringComparison.Ordinal);
                        sb.Append("__hci_pair:");
                        sb.Append(rel);
                        sb.AppendLine();
                    }
                }
            }

            if (ext == ".razor")
            {
                sb.AppendLine("__hci_kind:razor");

                if (Regex.IsMatch(text, @"(?m)^\s*@code\b"))
                    sb.AppendLine("__hci_razor:has_code_block");

                foreach (Match m in Regex.Matches(text, @"(?m)^\s*@page\s+(?<route>.+?)\s*$"))
                {
                    var route = m.Groups["route"].Value.Trim().Trim('"', '\'');
                    if (route.Length > 0)
                    {
                        sb.Append("__hci_page:");
                        sb.Append(route);
                        sb.AppendLine();
                    }
                }

                foreach (Match m in Regex.Matches(text, @"(?m)^\s*@layout\s+(?<layout>\S+)\s*$"))
                {
                    var layout = m.Groups["layout"].Value.Trim().Trim('"', '\'');
                    if (layout.Length > 0)
                    {
                        sb.Append("__hci_layout:");
                        sb.Append(layout);
                        sb.AppendLine();
                    }
                }

                var nsCount = 0;
                foreach (Match m in Regex.Matches(text, @"(?m)^\s*@namespace\s+(?<ns>\S+)\s*$"))
                {
                    var ns = m.Groups["ns"].Value.Trim();
                    if (ns.Length > 0)
                    {
                        sb.Append("__hci_namespace:");
                        sb.Append(ns);
                        sb.AppendLine();
                    }

                    if (++nsCount >= 4)
                        break;
                }

                var usingCount = 0;
                foreach (Match m in Regex.Matches(text, @"(?m)^\s*@using\s+(?<u>[^\r\n]+?)\s*$"))
                {
                    var u = m.Groups["u"].Value.Trim().Trim('"', '\'');
                    if (u.Length > 0)
                    {
                        sb.Append("__hci_using:");
                        sb.Append(u);
                        sb.AppendLine();
                    }

                    if (++usingCount >= 40)
                        break;
                }

                foreach (Match m in Regex.Matches(text, @"(?m)^\s*@implements\s+(?<t>.+?)\s*$"))
                {
                    var t = m.Groups["t"].Value.Trim().Trim('"', '\'');
                    if (t.Length > 0)
                    {
                        sb.Append("__hci_implements:");
                        sb.Append(t);
                        sb.AppendLine();
                    }
                }

                foreach (Match m in Regex.Matches(text, @"(?m)^\s*@inherits\s+(?<b>\S+)\s*$"))
                {
                    var b = m.Groups["b"].Value.Trim().Trim('"', '\'');
                    if (b.Length > 0)
                    {
                        sb.Append("__hci_inherits:");
                        sb.Append(b);
                        sb.AppendLine();
                    }
                }

                foreach (Match m in Regex.Matches(text, @"(?m)^\s*@typeparam\s+(?<p>\w+)\s*$"))
                {
                    var p = m.Groups["p"].Value.Trim();
                    if (p.Length > 0)
                    {
                        sb.Append("__hci_typeparam:");
                        sb.Append(p);
                        sb.AppendLine();
                    }
                }

                var handlers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in Regex.Matches(text, @"\b@(on[a-z]+)\b"))
                {
                    var ev = m.Groups[1].Value;
                    if (ev.Length <= 2)
                        continue;
                    var token = "@" + ev;
                    if (!handlers.Add(token))
                        continue;
                    sb.Append("__hci_handler:");
                    sb.Append(token);
                    sb.AppendLine();
                }

                var attrCount = 0;
                foreach (Match m in Regex.Matches(text, @"(?m)^\s*@attribute\s+\[(?<inner>[^\]]+)\]"))
                {
                    var inner = m.Groups["inner"].Value.Trim();
                    if (inner.Length > 0)
                    {
                        sb.Append("__hci_attribute:");
                        sb.Append(inner);
                        sb.AppendLine();
                    }

                    if (++attrCount >= 24)
                        break;
                }

                var injectCount = 0;
                foreach (Match m in Regex.Matches(text, @"(?m)^\s*@inject\s+(?<type>\S+)\s+(?<name>\S+)\s*$"))
                {
                    var type = m.Groups["type"].Value.Trim();
                    var name = m.Groups["name"].Value.Trim();
                    if (type.Length > 0 && name.Length > 0)
                    {
                        sb.Append("__hci_inject:");
                        sb.Append(type);
                        sb.Append(' ');
                        sb.Append(name);
                        sb.AppendLine();
                    }

                    if (++injectCount >= 32)
                        break;
                }

                // [Parameter] public Type Name { — best-effort for .razor @code sections
                var paramCount = 0;
                foreach (Match m in Regex.Matches(text, @"\[\s*Parameter(?:\([^\]]*\))?\s*\]\s*(?:public\s+)?[\w<>,\[\]\s\.]+\s+(?<id>\w+)\s*\{", RegexOptions.Singleline))
                {
                    var id = m.Groups["id"].Value.Trim();
                    if (id.Length > 0 && char.IsUpper(id[0]))
                    {
                        sb.Append("__hci_parameter:");
                        sb.Append(id);
                        sb.AppendLine();
                    }

                    if (++paramCount >= 48)
                        break;
                }

                var comps = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match m in Regex.Matches(text, @"<(?<tag>[A-Z][A-Za-z0-9_\.]+)\b"))
                {
                    var tag = m.Groups["tag"].Value;
                    if (tag.Length > 0)
                        comps.Add(tag);
                    if (comps.Count >= 50)
                        break;
                }

                foreach (var c in comps)
                {
                    sb.Append("__hci_component:");
                    sb.Append(c);
                    sb.AppendLine();
                }
            }

            if (ext == ".axaml")
            {
                sb.AppendLine("__hci_kind:axaml");

                foreach (Match m in Regex.Matches(text, @"\bx:Class\s*=\s*""(?<c>[^""]+)"""))
                {
                    var c = m.Groups["c"].Value.Trim();
                    if (c.Length > 0)
                    {
                        sb.Append("__hci_xclass:");
                        sb.Append(c);
                        sb.AppendLine();
                    }
                }

                var xmlnsCount = 0;
                foreach (Match m in Regex.Matches(text, @"xmlns\s*:\s*(?<p>\w+)\s*=\s*""(?<u>[^""]+)"""))
                {
                    var p = m.Groups["p"].Value.Trim();
                    var u = m.Groups["u"].Value.Trim();
                    if (p.Length > 0 && u.Length > 0)
                    {
                        sb.Append("__hci_xmlns:");
                        sb.Append(p);
                        sb.Append('=');
                        sb.Append(u);
                        sb.AppendLine();
                    }

                    if (++xmlnsCount >= 28)
                        break;
                }

                var resCount = 0;
                foreach (Match m in Regex.Matches(text, @"\{StaticResource\s+(?<r>[^}]+)\}"))
                {
                    var r = m.Groups["r"].Value.Trim();
                    if (r.Length > 0)
                    {
                        sb.Append("__hci_staticresource:");
                        sb.Append(r);
                        sb.AppendLine();
                    }

                    if (++resCount >= 48)
                        break;
                }

                var dynResCount = 0;
                foreach (Match m in Regex.Matches(text, @"\{DynamicResource\s+(?<r>[^}]+)\}"))
                {
                    var r = m.Groups["r"].Value.Trim();
                    if (r.Length > 0)
                    {
                        sb.Append("__hci_dynamicresource:");
                        sb.Append(r);
                        sb.AppendLine();
                    }

                    if (++dynResCount >= 48)
                        break;
                }

                var keyCount = 0;
                foreach (Match m in Regex.Matches(text, @"\bx:Key\s*=\s*""(?<k>[^""]+)"""))
                {
                    var k = m.Groups["k"].Value.Trim();
                    if (k.Length > 0)
                    {
                        sb.Append("__hci_key:");
                        sb.Append(k);
                        sb.AppendLine();
                    }

                    if (++keyCount >= 64)
                        break;
                }

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in Regex.Matches(text, @"\bx:Name\s*=\s*""(?<n>[^""]+)"""))
                {
                    var n = m.Groups["n"].Value.Trim();
                    if (n.Length > 0)
                        names.Add(n);
                    if (names.Count >= 80)
                        break;
                }

                foreach (var n in names)
                {
                    sb.Append("__hci_xname:");
                    sb.Append(n);
                    sb.AppendLine();
                }

                var binds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in Regex.Matches(text, @"\{Binding\s+(?<p>[^}\s,]+)"))
                {
                    var p = m.Groups["p"].Value.Trim();
                    if (p.Length > 0)
                        binds.Add(p);
                    if (binds.Count >= 80)
                        break;
                }

                foreach (var b in binds)
                {
                    sb.Append("__hci_binding:");
                    sb.Append(b);
                    sb.AppendLine();
                }

                foreach (Match m in Regex.Matches(text, @"\bClasses\s*=\s*""(?<c>[^""]+)"""))
                {
                    var cls = m.Groups["c"].Value.Trim();
                    if (cls.Length > 0)
                    {
                        sb.Append("__hci_classes:");
                        sb.Append(cls);
                        sb.AppendLine();
                    }
                }

                foreach (Match m in Regex.Matches(text, @"avares:[^\s""']+"))
                {
                    var uri = m.Value.Trim();
                    if (uri.Length > 0)
                    {
                        sb.Append("__hci_avares:");
                        sb.Append(uri);
                        sb.AppendLine();
                    }
                }
            }

            if (sb.Length == 0)
                return "";

            // Separate header from original content, so snippets remain readable.
            sb.AppendLine();
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static bool IsFileChanged(SqliteConnection conn, SqliteTransaction tx, string path, long sizeBytes, long lastWriteUtcTicks)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT size_bytes, last_write_utc_ticks FROM file_state WHERE path=$p LIMIT 1;";
        cmd.Parameters.AddWithValue("$p", path);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return true;
        var prevSize = r.GetInt64(0);
        var prevTicks = r.GetInt64(1);
        return prevSize != sizeBytes || prevTicks != lastWriteUtcTicks;
    }

    private static void UpsertFileState(SqliteConnection conn, SqliteTransaction tx, string path, long sizeBytes, long lastWriteUtcTicks)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO file_state(path, size_bytes, last_write_utc_ticks)
            VALUES($p, $s, $t)
            ON CONFLICT(path) DO UPDATE SET size_bytes=excluded.size_bytes, last_write_utc_ticks=excluded.last_write_utc_ticks;
            """;
        cmd.Parameters.AddWithValue("$p", path);
        cmd.Parameters.AddWithValue("$s", sizeBytes);
        cmd.Parameters.AddWithValue("$t", lastWriteUtcTicks);
        cmd.ExecuteNonQuery();
    }

    private static void DeleteFileState(SqliteConnection conn, SqliteTransaction tx, string path)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM file_state WHERE path=$p;";
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();
    }

    private static IEnumerable<string> EnumerateStalePaths(SqliteConnection conn, SqliteTransaction tx, HashSet<string> seen)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT path FROM file_state;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var p = r.GetString(0);
            if (!seen.Contains(p))
                yield return p;
        }
    }

    private static void DeleteChunksForPath(SqliteConnection conn, SqliteTransaction tx, string path)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM chunks WHERE path=$p;";
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();
    }

    private static void InsertChunk(
        SqliteConnection conn,
        SqliteTransaction tx,
        string path,
        string ext,
        int lineStart,
        int lineEnd,
        string body)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO chunks(path, extension, line_start, line_end, body)
            VALUES ($path, $ext, $ls, $le, $body);
            """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$ext", ext);
        cmd.Parameters.AddWithValue("$ls", lineStart);
        cmd.Parameters.AddWithValue("$le", lineEnd);
        cmd.Parameters.AddWithValue("$body", body);
        cmd.ExecuteNonQuery();
    }

    private static void AddSkip(
        List<SkippedPath> sample,
        Dictionary<string, int> reasonCounts,
        Dictionary<string, int> prefixCounts,
        string relPath,
        string reason)
    {
        reasonCounts[reason] = reasonCounts.TryGetValue(reason, out var c) ? c + 1 : 1;
        var pfx = GetPathPrefix(relPath);
        prefixCounts[pfx] = prefixCounts.TryGetValue(pfx, out var pc) ? pc + 1 : 1;

        if (sample.Count >= 50)
            return;
        sample.Add(new SkippedPath(relPath, reason));
    }

    private static string GetPathPrefix(string relPath)
    {
        var p = relPath.Replace("\\", "/", StringComparison.Ordinal);
        var idx = p.IndexOf('/', StringComparison.Ordinal);
        return idx <= 0 ? p : p[..idx];
    }

    private static IReadOnlyList<(string PathPrefix, int Count)> TopPrefixes(Dictionary<string, int> prefixCounts)
        => prefixCounts
            .OrderByDescending(static kv => kv.Value)
            .ThenBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(static kv => (kv.Key, kv.Value))
            .ToArray();

    internal static void NotifyFileIndexed(
        IReadOnlyList<ICodebaseIndexReindexObserver>? observers,
        string workspaceRoot,
        string relativePathUnix,
        string absolutePath,
        string extension,
        string text,
        long lastWriteUtcTicks)
    {
        if (observers is null || observers.Count == 0)
            return;

        var evt = new IndexedFileEvent(
            workspaceRoot,
            relativePathUnix,
            absolutePath,
            extension,
            text,
            lastWriteUtcTicks);

        foreach (var observer in observers)
        {
            try
            {
                observer.OnFileIndexed(evt);
            }
            catch
            {
                // best-effort: observers must not break reindex
            }
        }
    }
}

