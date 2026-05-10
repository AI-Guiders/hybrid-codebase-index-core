using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace HybridCodebaseIndex.Core;

internal static partial class SqliteFtsIndex
{
    private static ReindexSummary ReindexIncremental(string workspaceRoot, string dbPath, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        workspaceRoot = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));

        var filesIndexed = 0;
        var skippedLarge = 0;
        var skippedBinary = 0;
        var skippedExcluded = 0;
        var skippedSample = new List<SkippedPath>(capacity: 64);
        var skippedReasonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var skippedTopPathPrefixes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate");
        conn.Open();

        EnsureMetaTable(conn);
        EnsureChunksTable(conn);
        EnsureFileStateTable(conn);

        try
        {
            UpsertMeta(conn, "reindex_state", "running");
            UpsertMeta(conn, "reindex_started_at", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

            var settings = IndexSettings.TryLoadFromIndexDirectory(Path.GetDirectoryName(dbPath)!);
            var extensions = settings.GetEffectiveExtensions();
            var maxBytes = settings.GetEffectiveMaxIndexedFileBytes();
            var chunkLines = settings.GetEffectiveChunkLines();
            var overlapLines = settings.GetEffectiveChunkOverlapLines();
            var probeBytes = settings.GetEffectiveBinaryProbeBytes();

            var roots = new List<string>(capacity: 1 + settings.ExtraIncludeRoots.Count) { workspaceRoot };
            foreach (var extra in settings.ExtraIncludeRoots)
            {
                var p = Path.GetFullPath(Path.Combine(workspaceRoot, extra));
                if (Directory.Exists(p))
                    roots.Add(p);
            }

            var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

            var excludeRootFullPaths = new List<string>(settings.ExcludeRoots.Count);
            foreach (var rel in settings.ExcludeRoots)
            {
                var p = rel?.Trim();
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                if (Path.IsPathRooted(p) || p.Contains("..", StringComparison.Ordinal))
                    continue;
                var abs = Path.GetFullPath(Path.Combine(workspaceRoot, p));
                if (Directory.Exists(abs))
                    excludeRootFullPaths.Add(abs.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            }

            var candidates = new List<string>(capacity: 8192);
            foreach (var root in roots)
                candidates.AddRange(WorkspaceScanner.EnumerateIndexableFiles(root, extSet, settings.ExcludePathSegments, excludeRootFullPaths));

            var gitIgnore = GitIgnoreRules.TryLoad(workspaceRoot, settings.IgnoreFiles);

            var seen = new HashSet<string>(StringComparer.Ordinal);

            using var tx = conn.BeginTransaction();

            foreach (var absolute in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (WorkspaceScanner.ShouldExcludePath(absolute, settings.ExcludePathSegments))
                {
                    skippedExcluded++;
                    AddSkip(skippedSample, skippedReasonCounts, skippedTopPathPrefixes, WorkspaceScanner.RelativePath(workspaceRoot, absolute), "denylist");
                    continue;
                }

                var rel = WorkspaceScanner.RelativePath(workspaceRoot, absolute).Replace("\\", "/", StringComparison.Ordinal);
                seen.Add(rel);

                if (GitIgnoreRules.IsIgnored(gitIgnore, rel))
                {
                    skippedExcluded++;
                    AddSkip(skippedSample, skippedReasonCounts, skippedTopPathPrefixes, rel, "gitignore");
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(absolute);
                    if (info.Length > maxBytes)
                    {
                        skippedLarge++;
                        AddSkip(skippedSample, skippedReasonCounts, skippedTopPathPrefixes, rel, "too_large");
                        continue;
                    }
                }
                catch
                {
                    skippedExcluded++;
                    AddSkip(skippedSample, skippedReasonCounts, skippedTopPathPrefixes, rel, "io_error");
                    continue;
                }

                var lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks;
                if (!IsFileChanged(conn, tx, rel, info.Length, lastWriteUtcTicks))
                    continue;

                using var fs = info.OpenRead();
                var probeSize = (int)Math.Min(probeBytes, info.Length);
                var probe = new byte[probeSize];
                var read = fs.ReadAtLeast(probe.AsSpan(0, probeSize), probeSize, throwOnEndOfStream: false);
                if (WorkspaceScanner.LooksBinary(probe.AsSpan(0, read)))
                {
                    skippedBinary++;
                    AddSkip(skippedSample, skippedReasonCounts, skippedTopPathPrefixes, rel, "binary");
                    continue;
                }

                fs.Position = 0;
                using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var text = reader.ReadToEnd();

                // Replace chunks for this path.
                DeleteChunksForPath(conn, tx, rel);

                var ext = Path.GetExtension(absolute);
                var header = BuildArtifactAugmentationHeader(workspaceRoot, absolute, rel, ext, text);
                var chunks = WorkspaceScanner.ChunkByLines(text, chunkLines, overlapLines);
                var anyChunk = false;
                foreach (var (lineStart, lineEnd, body) in chunks)
                {
                    var augmentedBody = lineStart == 1 && header.Length > 0 ? header + body : body;
                    InsertChunk(conn, tx, rel, ext, lineStart, lineEnd, augmentedBody);
                    anyChunk = true;
                }

                if (anyChunk)
                    filesIndexed++;

                UpsertFileState(conn, tx, rel, info.Length, lastWriteUtcTicks);
            }

            // Remove deleted files.
            foreach (var stale in EnumerateStalePaths(conn, tx, seen))
            {
                DeleteChunksForPath(conn, tx, stale);
                DeleteFileState(conn, tx, stale);
            }

            tx.Commit();

            UpsertMeta(conn, "format_version", FormatVersion.ToString(CultureInfo.InvariantCulture));
            UpsertMeta(conn, "indexed_at", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            UpsertMeta(conn, "workspace_root", workspaceRoot);
            UpsertMeta(conn, "reindex_error", "");
            UpsertMeta(conn, "reindex_error_at", "");
            UpsertMeta(conn, "reindex_state", "idle");
            UpsertMeta(conn, "reindex_started_at", "");
        }
        catch (Exception ex)
        {
            try
            {
                UpsertMeta(conn, "reindex_state", "error");
                UpsertMeta(conn, "reindex_error", ex.GetType().Name + ": " + ex.Message);
                UpsertMeta(conn, "reindex_error_at", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            }
            catch
            {
                // ignore
            }

            throw;
        }

        sw.Stop();
        return new ReindexSummary(
            FormatVersion,
            dbPath,
            filesIndexed,
            skippedLarge,
            skippedBinary,
            skippedExcluded,
            skippedReasonCounts,
            TopPrefixes(skippedTopPathPrefixes),
            skippedSample,
            sw.Elapsed);
    }
}

