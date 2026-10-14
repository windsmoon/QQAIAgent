using QQAIAgent.Messaging;

namespace QQAIAgent.Messaging.Content;

public class ForwardContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.Forward;
    /// <summary>合并转发资源 ID，内容需在处理阶段调 get_forward_msg 展开。</summary>
    public string ResourceId { get; set; } = "";
    /// <summary>内层转发的消息节点。NapCat 展开外层转发时会把内层内容随 data.content 内联返回，此时无需再调接口。</summary>
    public List<GroupMessage> InnerMessages { get; set; } = [];
    #endregion
}
