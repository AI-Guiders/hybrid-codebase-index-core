using System.Collections.Frozen;
using System.Text;

namespace HybridCodebaseIndex.Core.Embeddings;

/// <summary>
/// Minimal BERT-style tokenizer for sentence-transformers WordPiece vocab.txt.
/// Intentionally small: enough for local embeddings without extra dependencies.
/// </summary>
internal sealed class WordPieceTokenizer
{
    private readonly FrozenDictionary<string, int> _vocab;
    private readonly bool _doLowerCase;
    private readonly int _unkId;
    private readonly int _clsId;
    private readonly int _sepId;
    private readonly int _padId;

    public WordPieceTokenizer(IReadOnlyDictionary<string, int> vocab, bool doLowerCase)
    {
        _vocab = vocab.ToFrozenDictionary(StringComparer.Ordinal);
        _doLowerCase = doLowerCase;

        _unkId = GetRequiredId("[UNK]");
        _clsId = GetRequiredId("[CLS]");
        _sepId = GetRequiredId("[SEP]");
        _padId = _vocab.TryGetValue("[PAD]", out var pad) ? pad : 0;
    }

    public static WordPieceTokenizer FromVocabFile(string vocabPath, bool doLowerCase)
    {
        if (string.IsNullOrWhiteSpace(vocabPath))
            throw new ArgumentException("embedding_vocab_path is required for embedding_provider=onnx with WordPiece models.", nameof(vocabPath));
        if (!File.Exists(vocabPath))
            throw new FileNotFoundException("vocab.txt not found.", vocabPath);

        var dict = new Dictionary<string, int>(capacity: 32_000, comparer: StringComparer.Ordinal);
        var i = 0;
        foreach (var line in File.ReadLines(vocabPath))
        {
            var token = line.TrimEnd('\r', '\n');
            if (token.Length == 0)
                continue;
            dict[token] = i++;
        }

        return new WordPieceTokenizer(dict, doLowerCase);
    }

    public (long[] inputIds, long[] attentionMask, long[] tokenTypeIds) Encode(string text, int seqLen)
    {
        seqLen = Math.Clamp(seqLen, 8, 512);

        var ids = new long[seqLen];
        var attn = new long[seqLen];
        var typeIds = new long[seqLen]; // all zeros for single-sequence

        ids[0] = _clsId;
        attn[0] = 1;

        var pos = 1;
        foreach (var token in BasicTokenize(text))
        {
            if (pos >= seqLen - 1)
                break;

            foreach (var subId in WordPieceTokenizeToIds(token))
            {
                if (pos >= seqLen - 1)
                    break;
                ids[pos] = subId;
                attn[pos] = 1;
                pos++;
            }
        }

        if (pos < seqLen)
        {
            ids[pos] = _sepId;
            attn[pos] = 1;
            pos++;
        }

        for (var i = pos; i < seqLen; i++)
            ids[i] = _padId;

        return (ids, attn, typeIds);
    }

    private IEnumerable<string> BasicTokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var s = _doLowerCase ? text.ToLowerInvariant() : text;

        var sb = new StringBuilder(capacity: 64);
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                continue;
            }

            if (IsPunctuation(ch))
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                yield return ch.ToString();
                continue;
            }

            sb.Append(ch);
        }
        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private IEnumerable<int> WordPieceTokenizeToIds(string token)
    {
        if (string.IsNullOrEmpty(token))
            yield break;

        // fast-path: exact match
        if (_vocab.TryGetValue(token, out var exact))
        {
            yield return exact;
            yield break;
        }

        const int maxChars = 200;
        if (token.Length > maxChars)
        {
            yield return _unkId;
            yield break;
        }

        var start = 0;
        var isBad = false;
        var subTokens = new List<int>(capacity: 8);

        while (start < token.Length)
        {
            var end = token.Length;
            int? curId = null;

            while (start < end)
            {
                var substr = token[start..end];
                if (start > 0)
                    substr = "##" + substr;

                if (_vocab.TryGetValue(substr, out var id))
                {
                    curId = id;
                    break;
                }
                end--;
            }

            if (curId is null)
            {
                isBad = true;
                break;
            }

            subTokens.Add(curId.Value);
            start = end;
        }

        if (isBad)
        {
            yield return _unkId;
            yield break;
        }

        foreach (var id in subTokens)
            yield return id;
    }

    private int GetRequiredId(string token)
    {
        if (_vocab.TryGetValue(token, out var id))
            return id;
        throw new InvalidOperationException($"Tokenizer vocab is missing required token '{token}'.");
    }

    private static bool IsPunctuation(char ch)
        => char.IsPunctuation(ch) || ch is '«' or '»' or '…';
}

