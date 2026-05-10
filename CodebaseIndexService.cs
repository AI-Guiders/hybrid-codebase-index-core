namespace HybridCodebaseIndex.Core;

/// <summary>Сервис гибридного индекса (слой B по ADR 0105 CascadeIDE; v0: FTS5 по тексту файлов).</summary>
public sealed class CodebaseIndexService
{
    private readonly string _indexDirectoryRelative;

    public CodebaseIndexService(string indexDirectoryRelative = ".hybrid-codebase-index")
    {
        _indexDirectoryRelative = string.IsNullOrWhiteSpace(indexDirectoryRelative)
            ? ".hybrid-codebase-index"
            : indexDirectoryRelative;
    }

    public string GetDatabasePath(string workspaceRoot)
        => GetDatabasePath(workspaceRoot, solutionPath: null);

    public string GetDatabasePath(string workspaceRoot, string? solutionPath)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var indexDir = ResolveIndexDirectoryRelative(root, solutionPath);
        return SqliteFtsIndex.ResolveDatabasePathForRead(root, indexDir);
    }

    public Task<ReindexSummary> FullReindexAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        => FullReindexAsync(workspaceRoot, solutionPath: null, cancellationToken);

    public Task<ReindexSummary> FullReindexAsync(string workspaceRoot, string? solutionPath, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var indexDir = ResolveIndexDirectoryRelative(root, solutionPath);
        var db = SqliteFtsIndex.ResolveDatabasePathForWrite(root, indexDir);
        return SqliteFtsIndex.ReindexIncrementalAsync(root, db, cancellationToken);
    }

    public Task<ReindexSummary> FullRebuildAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        => FullRebuildAsync(workspaceRoot, solutionPath: null, cancellationToken);

    public Task<ReindexSummary> FullRebuildAsync(string workspaceRoot, string? solutionPath, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var indexDir = ResolveIndexDirectoryRelative(root, solutionPath);
        var db = SqliteFtsIndex.ResolveDatabasePathForWrite(root, indexDir);
        return SqliteFtsIndex.FullRebuildAsync(root, db, cancellationToken);
    }

    public Task<ExplainHitResponse> ExplainHitAsync(
        string workspaceRoot,
        long hitId,
        CancellationToken cancellationToken = default)
        => ExplainHitAsync(workspaceRoot, solutionPath: null, hitId, cancellationToken);

    public Task<ExplainHitResponse> ExplainHitAsync(
        string workspaceRoot,
        string? solutionPath,
        long hitId,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var indexDir = ResolveIndexDirectoryRelative(root, solutionPath);
        var db = SqliteFtsIndex.ResolveDatabasePathForRead(root, indexDir);
        return SqliteFtsIndex.ExplainHitAsync(root, db, hitId, cancellationToken);
    }

    public Task<(SearchResponse response, string? error)> SearchAsync(
        string workspaceRoot,
        string query,
        int topN = 15,
        string? pathPrefix = null,
        IReadOnlyList<string>? excludePathPrefixes = null,
        IReadOnlyList<string>? extensions = null,
        CancellationToken cancellationToken = default)
        => SearchAsync(workspaceRoot, solutionPath: null, query, topN, pathPrefix, excludePathPrefixes, extensions, cancellationToken);

    public Task<(SearchResponse response, string? error)> SearchAsync(
        string workspaceRoot,
        string? solutionPath,
        string query,
        int topN = 15,
        string? pathPrefix = null,
        IReadOnlyList<string>? excludePathPrefixes = null,
        IReadOnlyList<string>? extensions = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var indexDir = ResolveIndexDirectoryRelative(root, solutionPath);
        var db = SqliteFtsIndex.ResolveDatabasePathForRead(root, indexDir);
        return SqliteFtsIndex.SearchAsync(root, db, query, topN, pathPrefix, excludePathPrefixes, extensions, cancellationToken);
    }

    public Task<(SearchResponse response, string? error)> SearchHybridAsync(
        string workspaceRoot,
        string? solutionPath,
        string query,
        int topN,
        string? pathPrefix,
        IReadOnlyList<string>? excludePathPrefixes,
        IReadOnlyList<string>? extensions,
        bool semantic,
        double alpha,
        double beta,
        int vecTopK,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var indexDir = ResolveIndexDirectoryRelative(root, solutionPath);
        var db = SqliteFtsIndex.ResolveDatabasePathForRead(root, indexDir);
        return SqliteFtsIndex.SearchHybridAsync(root, db, query, topN, pathPrefix, excludePathPrefixes, extensions, semantic, alpha, beta, vecTopK, cancellationToken);
    }

    public Task<IndexStatus> GetStatusAsync(string workspaceRoot, CancellationToken cancellationToken = default)
        => GetStatusAsync(workspaceRoot, solutionPath: null, cancellationToken);

    public Task<IndexStatus> GetStatusAsync(string workspaceRoot, string? solutionPath, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var indexDir = ResolveIndexDirectoryRelative(root, solutionPath);
        var db = SqliteFtsIndex.ResolveDatabasePathForRead(root, indexDir);
        return SqliteFtsIndex.GetStatusAsync(root, db, cancellationToken);
    }

    public Task<DocDraftResponse> DraftDocAsync(
        string workspaceRoot,
        string? solutionPath,
        string title,
        IReadOnlyList<string> changedPaths,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var indexDir = ResolveIndexDirectoryRelative(root, solutionPath);
        var db = SqliteFtsIndex.ResolveDatabasePathForRead(root, indexDir);
        return SqliteFtsIndex.DraftDocAsync(root, db, title, changedPaths, cancellationToken);
    }

    public Task<(int vectorsUpserted, string? err)> ReindexVectorsAsync(
        string workspaceRoot,
        string? solutionPath,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workspaceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var indexDir = ResolveIndexDirectoryRelative(root, solutionPath);
        var db = SqliteFtsIndex.ResolveDatabasePathForRead(root, indexDir);
        return SqliteFtsIndex.ReindexVectorsAsync(root, db, cancellationToken);
    }

    private string ResolveIndexDirectoryRelative(string workspaceRootNormalized, string? solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
            return _indexDirectoryRelative;

        var resolved = solutionPath.Trim();
        if (!Path.IsPathRooted(resolved))
            resolved = Path.Combine(workspaceRootNormalized, resolved);

        try
        {
            resolved = Path.GetFullPath(resolved);
        }
        catch
        {
            // best-effort: fall back to raw string (still scopes DB away from root)
        }

        if (OperatingSystem.IsWindows())
            resolved = resolved.ToLowerInvariant();

        var safeName = SanitizeSegment(Path.GetFileNameWithoutExtension(resolved));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "solution";

        var hash12 = ComputeHash12(resolved);
        var dir = $"{safeName}--{hash12}";
        return Path.Combine(_indexDirectoryRelative, dir);
    }

    private static string SanitizeSegment(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";

        Span<char> buffer = stackalloc char[s.Length];
        var n = 0;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                buffer[n++] = ch;
            else
                buffer[n++] = '-';
        }

        return new string(buffer[..n]).Trim('-');
    }

    private static string ComputeHash12(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
