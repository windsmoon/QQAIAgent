using Acme.Jieba;

namespace QQAIAgent.Infrastructure.Tokenization;

public sealed class Tokenizer
{
    #region fields
    private readonly JiebaSegmenter _segmenter;
    #endregion

    #region constructors
    /// <summary>初始化内置词典并按配置加载用户词典；后续分词复用此实例，不重复加载。</summary>
    public Tokenizer(TokenizerConfig config, string configDirectory)
    {
        _segmenter = new JiebaSegmenter();
        foreach (var dictionaryPath in config.UserDictionaryPaths)
        {
            _segmenter.LoadUserDict(Path.GetFullPath(dictionaryPath, configDirectory));
        }
    }
    #endregion

    #region methods
    public List<string> Tokenize(string text, string mode = "accurate")
    {
        var words = mode switch
        {
            "accurate" => _segmenter.Cut(text),
            "search" => _segmenter.CutForSearch(text, hmm: true),
            "full" => _segmenter.Cut(text, cutAll: true),
            _ => throw new ArgumentException("分词模式只能是 accurate、search 或 full。", nameof(mode))
        };

        // 完整枚举后返回，去除空白词元，保留其他词元的顺序、重复项和标点。
        return words.Where(word => string.IsNullOrWhiteSpace(word) == false).ToList();
    }
    #endregion
}
