using System.Text.Json;
using Tomlyn;

namespace HybridCodebaseIndex.Core;

public sealed record IndexSettings(
    bool IncludeCsInFts,
    IReadOnlyList<string> ExtraIncludeRoots,
    IReadOnlyList<string> ExcludeRoots,
    IReadOnlyList<string>? IncludeExtensions,
    IReadOnlyList<string>? ExcludeExtensions,
    IReadOnlyList<string> ExcludePathSegments,
    IReadOnlyList<string> IgnoreFiles,
    long MaxIndexedFileBytes,
    int ChunkLines,
    int ChunkOverlapLines,
    int BinaryProbeBytes,
    bool SemanticEnabled,
    string? EmbeddingProvider,
    string? EmbeddingModel,
    int EmbeddingDim,
    string? EmbeddingModelPath,
    string? EmbeddingVocabPath,
    bool EmbeddingDoLowerCase,
    string? VecExtensionsMode,
    IReadOnlyList<string>? VecExtensions,
    IReadOnlyList<string>? VecAddExtensions,
    IReadOnlyList<string>? VecRemoveExtensions,
    string? SqliteVecExtensionPath,
    int EmbeddingSequenceLength,
    bool EmbeddingPreferGpu)
{
    private static class HciTomlSerializer
    {
        private static readonly TomlSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        public static T? Deserialize<T>(string text) => TomlSerializer.Deserialize<T>(text, Options);
    }

    private sealed class SettingsTomlRoot
    {
        public ScopeToml? Scope { get; set; }
        public FtsToml? Fts { get; set; }
        public SemanticToml? Semantic { get; set; }
    }

    private sealed class ScopeToml
    {
        public List<string>? ExtraIncludeRoots { get; set; }
        public List<string>? ExcludeRoots { get; set; }
        public List<string>? ExcludePathSegments { get; set; }
        public List<string>? IgnoreFiles { get; set; }
    }

    private sealed class FtsToml
    {
        public bool? IncludeCsInFts { get; set; }
        public List<string>? IncludeExtensions { get; set; }
        public List<string>? ExcludeExtensions { get; set; }
        public long? MaxIndexedFileBytes { get; set; }
        public int? ChunkLines { get; set; }
        public int? ChunkOverlapLines { get; set; }
        public int? BinaryProbeBytes { get; set; }
    }

    private sealed class SemanticToml
    {
        public bool? Enabled { get; set; }
        public EmbeddingsToml? Embeddings { get; set; }

        public string? SqliteVecExtensionPath { get; set; }
        public string? VecExtensionsMode { get; set; }
        public List<string>? VecExtensions { get; set; }
        public List<string>? VecAddExtensions { get; set; }
        public List<string>? VecRemoveExtensions { get; set; }
    }

    private sealed class EmbeddingsToml
    {
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public int? Dim { get; set; }
        public string? ModelPath { get; set; }
        public string? VocabPath { get; set; }
        public bool? DoLowerCase { get; set; }
        public int? SequenceLength { get; set; }
        public bool? PreferGpu { get; set; }
    }

    /// <summary>
    /// Если merge настроек не дал ни одного расширения, используем тот же состав, что во встроенном
    /// <c>DefaultSettings/settings.default.toml</c> (без дублирования комментариев в коде — только список).
    /// </summary>
    private static readonly string[] FallbackIndexedExtensions =
    [
        ".md", ".mdx", ".csproj", ".slnx", ".props", ".targets", ".toml", ".editorconfig", ".json", ".yml",
        ".yaml", ".razor", ".css", ".scss", ".html", ".axaml", ".cs",
    ];

    public static IndexSettings Default { get; } = new(
        IncludeCsInFts: true,
        ExtraIncludeRoots: [],
        ExcludeRoots: [],
        IncludeExtensions: null,
        ExcludeExtensions: null,
        ExcludePathSegments: [],
        IgnoreFiles: [],
        MaxIndexedFileBytes: 0,
        ChunkLines: 0,
        ChunkOverlapLines: 0,
        BinaryProbeBytes: 0,
        SemanticEnabled: false,
        EmbeddingProvider: null,
        EmbeddingModel: null,
        EmbeddingDim: 0,
        EmbeddingModelPath: null,
        EmbeddingVocabPath: null,
        EmbeddingDoLowerCase: true,
        VecExtensionsMode: "inherit_fts",
        VecExtensions: null,
        VecAddExtensions: null,
        VecRemoveExtensions: null,
        SqliteVecExtensionPath: null,
        EmbeddingSequenceLength: 0,
        EmbeddingPreferGpu: true);

    public static IndexSettings TryLoadFromIndexDirectory(string? indexDirectory)
    {
        _ = TryLoadFromIndexDirectoryWithDiagnostics(indexDirectory, out var settings, out _, out _);
        return settings;
    }

    public static bool TryLoadFromIndexDirectoryWithDiagnostics(
        string? indexDirectory,
        out IndexSettings settings,
        out string settingsSource,
        out string? settingsParseError)
    {
        settings = Default;
        settingsSource = "default";
        settingsParseError = null;

        if (string.IsNullOrWhiteSpace(indexDirectory))
            return false;

        var dir = Path.GetFullPath(indexDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var path = Path.Combine(dir, "settings.toml");

        var embeddedModel = TryReadEmbeddedModel(out var embeddedErr);
        string? diskErr = null;
        var diskModel = File.Exists(path) ? TryReadDiskModel(path, out diskErr) : null;

        settingsParseError = diskErr ?? embeddedErr;

        if (embeddedModel is not null && diskModel is not null)
            settingsSource = "embedded+disk";
        else if (diskModel is not null)
            settingsSource = "disk";
        else if (embeddedModel is not null)
            settingsSource = "embedded";

        var merged = MergeModels(baseModel: embeddedModel, overlay: diskModel) ?? new SettingsTomlRoot();

        settings = FromMergedToml(merged);
        return true;
    }

    private static IndexSettings FromMergedToml(SettingsTomlRoot merged)
    {
        var includeCs = merged.Fts?.IncludeCsInFts ?? Default.IncludeCsInFts;

        var extraRoots = merged.Scope?.ExtraIncludeRoots ?? [];
        var excludeRoots = merged.Scope?.ExcludeRoots ?? [];
        var excludeSegments = merged.Scope?.ExcludePathSegments ?? [];
        var ignoreFiles = merged.Scope?.IgnoreFiles ?? [];

        var includeExt = NormalizeExtensions(merged.Fts?.IncludeExtensions);
        var excludeExt = NormalizeExtensions(merged.Fts?.ExcludeExtensions);

        var maxBytes = merged.Fts?.MaxIndexedFileBytes ?? Default.MaxIndexedFileBytes;
        var chunkLines = merged.Fts?.ChunkLines ?? Default.ChunkLines;
        var overlapLines = merged.Fts?.ChunkOverlapLines ?? Default.ChunkOverlapLines;
        var probeBytes = merged.Fts?.BinaryProbeBytes ?? Default.BinaryProbeBytes;

        var semanticEnabled = merged.Semantic?.Enabled ?? Default.SemanticEnabled;
        var embeddings = merged.Semantic?.Embeddings;
        var embeddingProvider = embeddings?.Provider ?? Default.EmbeddingProvider;
        var embeddingModel = embeddings?.Model ?? Default.EmbeddingModel;
        var embeddingDim = embeddings?.Dim ?? Default.EmbeddingDim;
        var embeddingModelPath = embeddings?.ModelPath ?? Default.EmbeddingModelPath;
        var embeddingVocabPath = embeddings?.VocabPath ?? Default.EmbeddingVocabPath;
        var embeddingDoLowerCase = embeddings?.DoLowerCase ?? Default.EmbeddingDoLowerCase;
        var embeddingSeqLen = embeddings?.SequenceLength ?? Default.EmbeddingSequenceLength;
        var embeddingPreferGpu = embeddings?.PreferGpu ?? Default.EmbeddingPreferGpu;

        var vecExtensionsMode = merged.Semantic?.VecExtensionsMode ?? Default.VecExtensionsMode;
        var vecExtensions = NormalizeExtensions(merged.Semantic?.VecExtensions);
        var vecAddExtensions = NormalizeExtensions(merged.Semantic?.VecAddExtensions);
        var vecRemoveExtensions = NormalizeExtensions(merged.Semantic?.VecRemoveExtensions);
        var sqliteVecExtensionPath = merged.Semantic?.SqliteVecExtensionPath ?? Default.SqliteVecExtensionPath;

        return new IndexSettings(
            includeCs,
            extraRoots,
            excludeRoots,
            includeExt,
            excludeExt,
            excludeSegments,
            ignoreFiles,
            maxBytes,
            chunkLines,
            overlapLines,
            probeBytes,
            semanticEnabled,
            embeddingProvider,
            embeddingModel,
            embeddingDim,
            embeddingModelPath,
            embeddingVocabPath,
            embeddingDoLowerCase,
            vecExtensionsMode,
            vecExtensions,
            vecAddExtensions,
            vecRemoveExtensions,
            sqliteVecExtensionPath,
            embeddingSeqLen,
            embeddingPreferGpu);
    }

    public long GetEffectiveMaxIndexedFileBytes()
        => MaxIndexedFileBytes > 0 ? MaxIndexedFileBytes : 512 * 1024;

    public int GetEffectiveChunkLines()
        => ChunkLines > 0 ? ChunkLines : 110;

    public int GetEffectiveChunkOverlapLines()
        => ChunkOverlapLines > 0 ? ChunkOverlapLines : 15;

    public int GetEffectiveBinaryProbeBytes()
        => BinaryProbeBytes > 0 ? BinaryProbeBytes : 8192;

    public IReadOnlyList<string> GetEffectiveExtensions()
    {
        IReadOnlyList<string> baseList;
        if (IncludeExtensions is { Count: > 0 })
        {
            baseList = IncludeExtensions;
        }
        else
        {
            // Пустой/null после merge: см. MergeFts — overlay `include_extensions = []` не затирает базу.
            // Если база тоже недоступна (редкий сбой embedded), не оставляем индекс без расширений.
            baseList = FallbackIndexedExtensions;
        }

        IEnumerable<string> filtered = baseList;
        if (ExcludeExtensions is { Count: > 0 })
        {
            var deny = new HashSet<string>(ExcludeExtensions, StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(e => !deny.Contains(e));
        }

        // include_cs_in_fts is a first-class toggle; treat it as stronger than include_extensions defaults.
        if (!IncludeCsInFts)
            filtered = filtered.Where(static e => !string.Equals(e, ".cs", StringComparison.OrdinalIgnoreCase));
        else if (baseList.Count > 0 && !filtered.Contains(".cs", StringComparer.OrdinalIgnoreCase))
            filtered = filtered.Concat([".cs"]);

        return filtered.ToArray();
    }

    /// <summary>Те же правила, что <see cref="GetEffectiveExtensions"/>, для vec и проверок на границе поиска.</summary>
    public HashSet<string> GetEffectiveExtensionsSet()
        => new(GetEffectiveExtensions(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Эффективные расширения для vec.
    /// По умолчанию наследует effective extensions от FTS и применяет add/remove.
    /// При <c>vec_extensions_mode="custom"</c> может использовать базовый список <c>semantic.vec_extensions</c>.
    /// </summary>
    public HashSet<string> GetEffectiveVecExtensionsSet()
    {
        HashSet<string> set;

        var mode = (VecExtensionsMode ?? "inherit_fts").Trim().ToLowerInvariant();
        if (string.Equals(mode, "custom", StringComparison.OrdinalIgnoreCase) && VecExtensions is { Count: > 0 })
            set = new HashSet<string>(VecExtensions, StringComparer.OrdinalIgnoreCase);
        else
            set = GetEffectiveExtensionsSet();

        if (VecRemoveExtensions is { Count: > 0 })
            foreach (var ext in VecRemoveExtensions)
                set.Remove(ext);

        if (VecAddExtensions is { Count: > 0 })
            foreach (var ext in VecAddExtensions)
                set.Add(ext);

        return set;
    }

    private static SettingsTomlRoot? TryReadEmbeddedModel(out string? error)
    {
        error = null;
        try
        {
            if (!BundledContent.TryReadEmbeddedText("DefaultSettings/settings.default.toml", out var embedded))
                return null;
            return HciTomlSerializer.Deserialize<SettingsTomlRoot>(embedded);
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
    }

    private static SettingsTomlRoot? TryReadDiskModel(string path, out string? error)
    {
        error = null;
        try
        {
            var text = File.ReadAllText(path);
            return HciTomlSerializer.Deserialize<SettingsTomlRoot>(text);
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
    }

    private static List<string>? NormalizeExtensions(List<string>? raw)
    {
        if (raw is null || raw.Count == 0)
            return raw;

        var list = new List<string>(raw.Count);
        foreach (var s0 in raw)
        {
            var s = s0.Trim();
            if (s.Length == 0)
                continue;
            if (!s.StartsWith(".", StringComparison.Ordinal))
                s = "." + s;
            list.Add(s.ToLowerInvariant());
        }
        return list;
    }

    private static SettingsTomlRoot? MergeModels(SettingsTomlRoot? baseModel, SettingsTomlRoot? overlay)
    {
        if (baseModel is null && overlay is null)
            return null;
        if (baseModel is null)
            return overlay;
        if (overlay is null)
            return baseModel;

        return new SettingsTomlRoot
        {
            Scope = MergeScope(baseModel.Scope, overlay.Scope),
            Fts = MergeFts(baseModel.Fts, overlay.Fts),
            Semantic = MergeSemantic(baseModel.Semantic, overlay.Semantic),
        };
    }

    private static ScopeToml? MergeScope(ScopeToml? a, ScopeToml? b)
    {
        if (a is null && b is null)
            return null;
        a ??= new ScopeToml();
        b ??= new ScopeToml();
        return new ScopeToml
        {
            ExtraIncludeRoots = b.ExtraIncludeRoots ?? a.ExtraIncludeRoots,
            ExcludeRoots = b.ExcludeRoots ?? a.ExcludeRoots,
            ExcludePathSegments = b.ExcludePathSegments ?? a.ExcludePathSegments,
            IgnoreFiles = b.IgnoreFiles ?? a.IgnoreFiles,
        };
    }

    private static FtsToml? MergeFts(FtsToml? a, FtsToml? b)
    {
        if (a is null && b is null)
            return null;
        a ??= new FtsToml();
        b ??= new FtsToml();
        return new FtsToml
        {
            IncludeCsInFts = b.IncludeCsInFts ?? a.IncludeCsInFts,
            // Пустой массив в overlay не должен затирать встроенный список (иначе индексируется 0 файлов).
            IncludeExtensions = b.IncludeExtensions is { Count: > 0 } ? b.IncludeExtensions : a.IncludeExtensions,
            ExcludeExtensions = b.ExcludeExtensions ?? a.ExcludeExtensions,
            MaxIndexedFileBytes = b.MaxIndexedFileBytes ?? a.MaxIndexedFileBytes,
            ChunkLines = b.ChunkLines ?? a.ChunkLines,
            ChunkOverlapLines = b.ChunkOverlapLines ?? a.ChunkOverlapLines,
            BinaryProbeBytes = b.BinaryProbeBytes ?? a.BinaryProbeBytes,
        };
    }

    private static SemanticToml? MergeSemantic(SemanticToml? a, SemanticToml? b)
    {
        if (a is null && b is null)
            return null;
        a ??= new SemanticToml();
        b ??= new SemanticToml();
        return new SemanticToml
        {
            Enabled = b.Enabled ?? a.Enabled,
            Embeddings = MergeEmbeddings(a.Embeddings, b.Embeddings),
            SqliteVecExtensionPath = b.SqliteVecExtensionPath ?? a.SqliteVecExtensionPath,
            VecExtensionsMode = b.VecExtensionsMode ?? a.VecExtensionsMode,
            VecExtensions = b.VecExtensions ?? a.VecExtensions,
            VecAddExtensions = b.VecAddExtensions ?? a.VecAddExtensions,
            VecRemoveExtensions = b.VecRemoveExtensions ?? a.VecRemoveExtensions,
        };
    }

    private static EmbeddingsToml? MergeEmbeddings(EmbeddingsToml? a, EmbeddingsToml? b)
    {
        if (a is null && b is null)
            return null;
        a ??= new EmbeddingsToml();
        b ??= new EmbeddingsToml();
        return new EmbeddingsToml
        {
            Provider = b.Provider ?? a.Provider,
            Model = b.Model ?? a.Model,
            Dim = b.Dim ?? a.Dim,
            ModelPath = b.ModelPath ?? a.ModelPath,
            VocabPath = b.VocabPath ?? a.VocabPath,
            DoLowerCase = b.DoLowerCase ?? a.DoLowerCase,
            SequenceLength = b.SequenceLength ?? a.SequenceLength,
            PreferGpu = b.PreferGpu ?? a.PreferGpu,
        };
    }
}

