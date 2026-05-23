namespace HybridCodebaseIndex.Core.Indexing;

/// <summary>
/// Наблюдатель за reindex в <see cref="CodebaseIndexService"/>.
/// Вызывается из фонового потока reindex — не блокировать; исключения глотаются Core.
/// </summary>
public interface ICodebaseIndexReindexObserver
{
    void OnFileIndexed(IndexedFileEvent file);
}
