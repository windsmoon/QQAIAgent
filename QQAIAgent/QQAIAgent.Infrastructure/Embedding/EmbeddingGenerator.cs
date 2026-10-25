using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Tokenizers.HuggingFace.Tokenizer;

namespace QQAIAgent.Infrastructure.Embedding;

/// <summary>复用 multilingual-e5-small 模型和 Tokenizer，生成归一化的 384 维文本向量。</summary>
public sealed class EmbeddingGenerator : IDisposable
{
    #region fields
    private const int Dimensions = 384;
    private readonly Tokenizer _tokenizer;
    private readonly InferenceSession _session;
    #endregion

    #region constructors
    public EmbeddingGenerator(string modelDirectory)
    {
        _tokenizer = Tokenizer.FromFile(Path.Combine(modelDirectory, "tokenizer.json"));
        try
        {
            _session = new InferenceSession(Path.Combine(modelDirectory, "model_int8.onnx"));
        }
        catch
        {
            _tokenizer.Dispose();
            throw;
        }
    }
    #endregion

    #region methods
    /// <summary>type 为 passage（聊天）或 query（查询）；单句超过 512 tokens 时截断并保留结束标记。</summary>
    public float[] Generate(string text, string type = "passage")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var prefix = type switch
        {
            "passage" => "passage: ",
            "query" => "query: ",
            _ => throw new ArgumentException("type 只能是 passage 或 query。", nameof(type))
        };
        var encoding = _tokenizer.Encode(prefix + text, true, includeTypeIds: true, includeAttentionMask: true).First();
        var length = Math.Min(encoding.Ids.Count, 512);
        var ids = new DenseTensor<long>([1, length]);
        var mask = new DenseTensor<long>([1, length]);
        var types = new DenseTensor<long>([1, length]);
        for (var index = 0; index < length; index++)
        {
            // 配套 Tokenizer 的单句格式是 <s>正文</s>，截断时将原末尾 </s> 放回最后一位。
            var sourceIndex = index == length - 1 ? encoding.Ids.Count - 1 : index;
            ids[0, index] = encoding.Ids[sourceIndex];
            mask[0, index] = encoding.AttentionMask[sourceIndex];
            types[0, index] = encoding.TypeIds[sourceIndex];
        }

        using var outputs = _session.Run([
            NamedOnnxValue.CreateFromTensor("input_ids", ids),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", types)
        ]);
        var hidden = outputs.First().AsTensor<float>();
        var vector = new float[Dimensions];
        var tokenCount = 0;
        for (var token = 0; token < length; token++)
        {
            if (mask[0, token] == 0)
            {
                continue;
            }
            tokenCount++;
            // 输出布局为 [1, token 数, 384]；padding 不参与平均，特殊 token 按模型要求保留。
            for (var dimension = 0; dimension < Dimensions; dimension++)
            {
                vector[dimension] += hidden.GetValue(token * Dimensions + dimension);
            }
        }

        for (var dimension = 0; dimension < Dimensions; dimension++)
        {
            vector[dimension] /= tokenCount;
        }
        var norm = Math.Sqrt(vector.Sum(value => (double)value * value));
        for (var dimension = 0; dimension < Dimensions; dimension++)
        {
            vector[dimension] = (float)(vector[dimension] / norm);
        }
        return vector;
    }

    public void Dispose()
    {
        _session.Dispose();
        _tokenizer.Dispose();
    }
    #endregion
}
