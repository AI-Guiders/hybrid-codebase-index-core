using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HybridCodebaseIndex.Core.Embeddings;

internal sealed class OnnxEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly InferenceSession _session;
    private readonly WordPieceTokenizer _tokenizer;
    private readonly string _inputIdsName;
    private readonly string _attentionMaskName;
    private readonly string? _tokenTypeIdsName;
    private readonly string _outputName;
    private readonly int _seqLen;
    private readonly object _gate = new();

    public int Dimension { get; }

    public OnnxEmbeddingProvider(string modelPath, string? vocabPath, bool doLowerCase, int seqLen, bool preferGpu)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("embedding_model_path is required for embedding_provider=onnx.", nameof(modelPath));
        if (string.IsNullOrWhiteSpace(vocabPath))
            throw new ArgumentException("embedding_vocab_path is required for embedding_provider=onnx.", nameof(vocabPath));

        _seqLen = Math.Clamp(seqLen, 32, 512);

        var opts = new SessionOptions();
        if (preferGpu)
        {
            try
            {
                // Optional: CUDA EP needs matching native runtime (GPU package / machine); otherwise catch → CPU.
                opts.AppendExecutionProvider_CUDA(0);
            }
            catch
            {
                // fall back to CPU
            }
        }

        _session = new InferenceSession(modelPath, opts);
        _tokenizer = WordPieceTokenizer.FromVocabFile(vocabPath, doLowerCase);

        var inputs = _session.InputMetadata;
        _inputIdsName = FindKey(inputs, ["input_ids", "inputIds", "input_ids:0"]) ?? inputs.Keys.First();
        _attentionMaskName = FindKey(inputs, ["attention_mask", "attentionMask"]) ?? inputs.Keys.Skip(1).FirstOrDefault() ?? "attention_mask";
        _tokenTypeIdsName = FindKey(inputs, ["token_type_ids", "tokenTypeIds"]);

        var outputs = _session.OutputMetadata;
        _outputName = FindKey(outputs, ["sentence_embedding", "embeddings", "pooled_output", "last_hidden_state"]) ?? outputs.Keys.First();

        // Infer dimension from output metadata when possible
        var dimsArr = outputs[_outputName].Dimensions.ToArray();
        // Common: [1, hidden] (pooled) or [1, seq, hidden] (last_hidden_state)
        Dimension = dimsArr.Length >= 2 && dimsArr[^1] > 0 ? dimsArr[^1] : 384;
    }

    public ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (ids, mask, typeIdsArr) = _tokenizer.Encode(text ?? "", _seqLen);

        var inputIds = new DenseTensor<long>(new[] { 1, _seqLen });
        var attn = new DenseTensor<long>(new[] { 1, _seqLen });
        var typeIds = _tokenTypeIdsName is null ? null : new DenseTensor<long>(new[] { 1, _seqLen });

        for (var i = 0; i < _seqLen; i++)
        {
            inputIds[0, i] = ids[i];
            attn[0, i] = mask[i];
            if (typeIds is not null)
                typeIds[0, i] = typeIdsArr[i];
        }

        var inputs = new List<NamedOnnxValue>(capacity: typeIds is null ? 2 : 3)
        {
            NamedOnnxValue.CreateFromTensor(_inputIdsName, inputIds),
            NamedOnnxValue.CreateFromTensor(_attentionMaskName, attn),
        };
        if (typeIds is not null && _tokenTypeIdsName is not null)
            inputs.Add(NamedOnnxValue.CreateFromTensor(_tokenTypeIdsName, typeIds));

        lock (_gate)
        {
            using var results = _session.Run(inputs);
            var first = results.First(r => string.Equals(r.Name, _outputName, StringComparison.OrdinalIgnoreCase) || r.Name == _outputName);

            // Try pooled embedding first
            if (first.Value is DenseTensor<float> pooled && pooled.Rank == 2)
            {
                var v = new float[pooled.Dimensions[1]];
                for (var j = 0; j < v.Length; j++)
                    v[j] = pooled[0, j];
                NormalizeInPlace(v);
                return ValueTask.FromResult(v);
            }

            // Fallback: mean pool last_hidden_state with attention mask
            var hs = first.AsTensor<float>();
            if (hs.Rank == 3)
            {
                var hidden = hs.Dimensions[2];
                var v = new float[hidden];
                double denom = 0;
                for (var t = 0; t < _seqLen; t++)
                {
                    if (attn[0, t] == 0)
                        continue;
                    denom += 1;
                    for (var j = 0; j < hidden; j++)
                        v[j] += hs[0, t, j];
                }
                if (denom > 0)
                {
                    var inv = (float)(1.0 / denom);
                    for (var j = 0; j < v.Length; j++)
                        v[j] *= inv;
                }
                NormalizeInPlace(v);
                return ValueTask.FromResult(v);
            }

            throw new InvalidOperationException($"Unexpected ONNX output shape for '{_outputName}'.");
        }
    }

    public void Dispose() => _session.Dispose();

    private static string? FindKey<T>(IReadOnlyDictionary<string, T> dict, string[] candidates)
    {
        foreach (var c in candidates)
        {
            foreach (var k in dict.Keys)
            {
                if (string.Equals(k, c, StringComparison.OrdinalIgnoreCase))
                    return k;
            }
        }
        return null;
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

