namespace QQAIAgent.Messaging.Content;

public class FaceContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.Face;
    /// <summary>QQ 系统表情 ID。</summary>
    public long Id { get; set; }
    /// <summary>表情名称；无法识别 ID 时为空。</summary>
    public string Name { get; set; } = "";
    #endregion
}
