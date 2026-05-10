namespace HybridCodebaseIndex.Core.Embeddings;

internal static class EmbeddingProviderFactory
{
    internal static IEmbeddingProvider Create(IndexSettings settings, string? indexDirectory = null)
    {
        var provider = (settings.EmbeddingProvider ?? "dummy").Trim().ToLowerInvariant();
        return provider switch
        {
            "dummy" => new DummyEmbeddingProvider(settings.EmbeddingDim > 0 ? settings.EmbeddingDim : 64),
            "onnx" => CreateOnnx(settings, indexDirectory),
            _ => new DummyEmbeddingProvider(settings.EmbeddingDim > 0 ? settings.EmbeddingDim : 64),
        };
    }

    private static IEmbeddingProvider CreateOnnx(IndexSettings settings, string? indexDirectory)
    {
        var modelPath = settings.EmbeddingModelPath;
        if (!string.IsNullOrWhiteSpace(modelPath) && !Path.IsPathRooted(modelPath) && !string.IsNullOrWhiteSpace(indexDirectory))
            modelPath = Path.Combine(indexDirectory, modelPath);

        var vocabPath = settings.EmbeddingVocabPath;
        if (!string.IsNullOrWhiteSpace(vocabPath) && !Path.IsPathRooted(vocabPath) && !string.IsNullOrWhiteSpace(indexDirectory))
            vocabPath = Path.Combine(indexDirectory, vocabPath);

        var seqLen = settings.EmbeddingSequenceLength > 0 ? settings.EmbeddingSequenceLength : 128;
        var preferGpu = settings.EmbeddingPreferGpu;
        return new OnnxEmbeddingProvider(
            modelPath: modelPath ?? "",
            vocabPath: vocabPath,
            doLowerCase: settings.EmbeddingDoLowerCase,
            seqLen: seqLen,
            preferGpu: preferGpu);
    }
}

