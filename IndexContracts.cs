namespace HybridCodebaseIndex.Core;

/// <summary>Стабильные значения типа попадания (слой B, ADR 0105 CascadeIDE). Векторный канал при включении semantic.</summary>
public static class HitKinds
{
    public const string TextFts = "text_fts";
    public const string TextVector = "text_vector";
}

public sealed record IndexHit(
    long HitId,
    string Path,
    string Extension,
    string HitKind,
    double RankScore,
    double? FtsScore,
    double? VecScore,
    string? Snippet,
    int LineStart,
    int LineEnd,
    int ChunkCharCount,
    string? LastWriteUtcIso);

public sealed record SearchResponse(
    int IndexFormatVersion,
    string Query,
    string DatabasePath,
    IReadOnlyList<IndexHit> Hits);

public sealed record ExplainHitResponse(
    int IndexFormatVersion,
    string DatabasePath,
    IndexHit? Hit,
    string? Err);

public sealed record IndexStatus(
    int IndexFormatVersion,
    string DatabasePath,
    bool DatabaseExists,
    int DocumentCount,
    bool DocumentCountMayBeStale,
    string? IndexedAtIso,
    string? WorkspaceRootNormalized,
    string? LastReindexError,
    string? LastReindexErrorAtIso,
    string SettingsSource,
    string? SettingsParseError,
    EffectiveSettings? EffectiveSettings,
    string? ReindexState,
    string? ReindexStartedAtIso);

public sealed record EffectiveSettings(
    bool IncludeCsInFts,
    IReadOnlyList<string> ExtraIncludeRoots,
    IReadOnlyList<string> ExcludeRoots,
    IReadOnlyList<string> EffectiveExtensions,
    IReadOnlyList<string> ExcludePathSegments,
    IReadOnlyList<string> IgnoreFiles,
    long MaxIndexedFileBytes,
    int ChunkLines,
    int ChunkOverlapLines,
    int BinaryProbeBytes);

public sealed record ReindexSummary(
    int IndexFormatVersion,
    string DatabasePath,
    int FilesIndexed,
    int FilesSkippedTooLarge,
    int FilesSkippedBinary,
    int FilesSkippedExcluded,
    IReadOnlyDictionary<string, int> SkippedReasonCounts,
    IReadOnlyList<(string PathPrefix, int Count)> SkippedTopPathPrefixes,
    IReadOnlyList<SkippedPath> SkippedSample,
    TimeSpan Duration);

public sealed record SkippedPath(
    string Path,
    string Reason);
