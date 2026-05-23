namespace HybridCodebaseIndex.Core.Indexing;

/// <summary>Событие: файл прочитан и проиндексирован в FTS (инкремент или full rebuild).</summary>
public sealed record IndexedFileEvent(
    string WorkspaceRoot,
    string RelativePathUnix,
    string AbsolutePath,
    string Extension,
    string Text,
    long LastWriteUtcTicks);
