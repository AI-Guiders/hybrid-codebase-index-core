using HybridCodebaseIndex.Core.Indexing;

namespace HybridCodebaseIndex.Core;

internal static partial class SqliteFtsIndex
{
    internal static Task<ReindexSummary> FullRebuildAsync(
        string workspaceRoot,
        string dbPath,
        IReadOnlyList<ICodebaseIndexReindexObserver>? reindexObservers,
        CancellationToken cancellationToken)
        => Task.Run(() => FullRebuild(workspaceRoot, dbPath, reindexObservers, cancellationToken), cancellationToken);

    internal static Task<ReindexSummary> ReindexIncrementalAsync(
        string workspaceRoot,
        string dbPath,
        IReadOnlyList<ICodebaseIndexReindexObserver>? reindexObservers,
        CancellationToken cancellationToken)
        => Task.Run(() => ReindexIncremental(workspaceRoot, dbPath, reindexObservers, cancellationToken), cancellationToken);
}
