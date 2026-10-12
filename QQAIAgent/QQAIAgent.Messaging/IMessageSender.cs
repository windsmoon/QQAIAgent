namespace QQAIAgent.Messaging;

public interface IMessageSender
{
    #region methods
    /// <summary>发送群文本消息，可引用指定消息并按需艾特用户，发送成功后返回消息 ID。</summary>
    Task<long> SendGroupMessageAsync(long groupId, long userId, string text, long? quoteMessageId = null, bool atUser = true, CancellationToken cancellationToken = default);
    #endregion
}
