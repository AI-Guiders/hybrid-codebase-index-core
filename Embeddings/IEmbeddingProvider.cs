namespace HybridCodebaseIndex.Core.Embeddings;

internal interface IEmbeddingProvider
{
    int Dimension { get; }
    ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}

