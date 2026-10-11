namespace QQAIAgent.Agent.Chat.Content;

public class ChatImageContent : ChatContent
{
    #region properties
    /// <summary>图片来源地址，供模型适配读取并上传图片。</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>模型服务上传后返回的文件 ID；未上传时为空，与消息来源的文件标识无关。</summary>
    public string FileId { get; set; } = string.Empty;
    #endregion
}
