namespace QQAIAgent.Tools.Config;

public sealed class BatConfig
{
    #region properties
    public string RootDirectory { get; set; } = "Data/Bat";
    /// <summary>脚本、cmd 代码页及输出解码使用同一编码，默认 UTF-8。</summary>
    public int CodePage { get; set; } = 65001;
    /// <summary>标准输出和错误输出各自最多保留的字符数。</summary>
    public int MaxOutputCharacters { get; set; } = 16000;
    #endregion
}
