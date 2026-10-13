namespace QQAIAgent.Messaging.Content;

public class ForwardContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.Forward;
    /// <summary>合并转发资源 ID，内容需在处理阶段调 get_forward_msg 展开。</summary>
    public string ResourceId { get; set; } = "";
    #endregion
}
