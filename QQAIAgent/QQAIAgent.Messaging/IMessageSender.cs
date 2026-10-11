namespace QQAIAgent.Messaging;

public interface IMessageSender
{
    #region methods
    /// <summary>发送艾特指定用户的群文本消息，发送成功后返回消息 ID。</summary>
    Task<long> SendGroupMessageAsync(long groupId, long userId, string text, CancellationToken cancellationToken);
    #endregion
}
