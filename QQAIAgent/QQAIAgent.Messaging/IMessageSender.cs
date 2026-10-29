namespace QQAIAgent.Messaging;

public interface IMessageSender
{
    #region methods
    /// <summary>发送群文本消息，可引用指定消息并按需艾特用户，也可附加艾特用户集合，发送成功后返回消息 ID。</summary>
    Task<long> SendGroupMessageAsync(long groupId, long userId, string text, long? quoteMessageId = null, bool atUser = true, CancellationToken cancellationToken = default, IReadOnlyCollection<long> atUserIds = null);
    /// <summary>通过网络地址发送群图片，不引用消息或艾特用户，每次重新下载图片，成功后返回消息 ID。</summary>
    Task<long> SendGroupImageAsync(long groupId, string imageUrl, CancellationToken cancellationToken = default);
    #endregion
}
