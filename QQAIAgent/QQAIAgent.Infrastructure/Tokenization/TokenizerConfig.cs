namespace QQAIAgent.Infrastructure.Tokenization;

public sealed class TokenizerConfig
{
    #region properties
    /// <summary>按顺序加载的用户词典路径；相对路径以配置文件所在目录为基准，空列表只使用内置词典。</summary>
    public List<string> UserDictionaryPaths { get; set; } = [];
    #endregion
}
