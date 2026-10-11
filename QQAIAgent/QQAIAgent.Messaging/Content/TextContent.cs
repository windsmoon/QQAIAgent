namespace QQAIAgent.Messaging.Content;

public class TextContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.Text;
    public string Text { get; set; } = "";
    #endregion
}
