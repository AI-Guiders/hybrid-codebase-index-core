using System.Security.Cryptography;
using System.Text;

namespace HybridCodebaseIndex.Core.Embeddings;

/// <summary>
/// Deterministic, dependency-free embedding for pipeline wiring.
/// Not semantic; use a real provider for meaning.
/// </summary>
internal sealed class DummyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimension { get; }

    public DummyEmbeddingProvider(int dimension = 64)
    {
        Dimension = Math.Clamp(dimension, 16, 256);
    }

    public ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Hash -> expand into floats in [-1..1], then L2-normalize.
        var bytes = Encoding.UTF8.GetBytes(text ?? "");
        var hash = SHA256.HashData(bytes);

        var v = new float[Dimension];
        var j = 0;
        while (j < Dimension)
        {
            // Mix hash with counter
            var tmp = new byte[hash.Length + 4];
            hash.CopyTo(tmp.AsSpan(0, hash.Length));
            BitConverter.TryWriteBytes(tmp.AsSpan(hash.Length, 4), j);
            var h2 = SHA256.HashData(tmp);

            for (var i = 0; i < h2.Length && j < Dimension; i += 2, j++)
            {
                var u16 = (ushort)(h2[i] | (h2[Math.Min(i + 1, h2.Length - 1)] << 8));
                v[j] = (u16 / 32767.5f) - 1f;
            }
        }

        NormalizeInPlace(v);
        return ValueTask.FromResult(v);
    }

    private static void NormalizeInPlace(float[] v)
    {
        double sum = 0;
        foreach (var x in v)
            sum += x * x;
        var norm = Math.Sqrt(sum);
        if (norm <= 1e-12)
            return;
        var inv = (float)(1.0 / norm);
        for (var i = 0; i < v.Length; i++)
            v[i] *= inv;
    }
}

