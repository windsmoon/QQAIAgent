namespace QQAIAgent.Messaging.Content;

public class JsonCardContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.JsonCard;
    /// <summary>QQ 官方生成的卡片摘要，如 "[链接]标题"；未提供时为空。</summary>
    public string Prompt { get; set; } = "";
    /// <summary>卡片标题；未提供时为空。</summary>
    public string Title { get; set; } = "";
    /// <summary>卡片描述；未提供时为空。</summary>
    public string Description { get; set; } = "";
    /// <summary>卡片跳转链接；未提供时为空。</summary>
    public string Url { get; set; } = "";
    #endregion
}
