namespace QQAIAgent.Messaging.Content;

public class AtContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.At;
    /// <summary>被 @ 的 QQ 号；"all" 表示 @ 全体成员。</summary>
    public string Target { get; set; } = "";
    #endregion
}
