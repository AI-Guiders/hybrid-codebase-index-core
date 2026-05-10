namespace HybridCodebaseIndex.Core;

internal static partial class SqliteFtsIndex
{
    internal static Task<ReindexSummary> FullRebuildAsync(
        string workspaceRoot,
        string dbPath,
        CancellationToken cancellationToken)
        => Task.Run(() => FullRebuild(workspaceRoot, dbPath, cancellationToken), cancellationToken);

    internal static Task<ReindexSummary> ReindexIncrementalAsync(
        string workspaceRoot,
        string dbPath,
        CancellationToken cancellationToken)
        => Task.Run(() => ReindexIncremental(workspaceRoot, dbPath, cancellationToken), cancellationToken);
}
