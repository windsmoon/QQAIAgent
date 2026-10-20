namespace QQAIAgent.Tools.Config;

public sealed class PythonConfig
{
    #region properties
    public string ExecutablePath { get; set; } = "Third/Python/python.exe";
    /// <summary>标准输出和错误输出各自最多保留的字符数。</summary>
    public int MaxOutputCharacters { get; set; } = 16000;
    #endregion
}
