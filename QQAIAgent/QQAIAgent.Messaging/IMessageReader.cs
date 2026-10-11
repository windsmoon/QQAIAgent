namespace QQAIAgent.Messaging;

public interface IMessageReader
{
    #region methods
    /// <summary>按消息 ID 获取群消息，保留正文消息段和引用关系。</summary>
    Task<GroupMessage> GetGroupMessageAsync(long messageId, CancellationToken cancellationToken);
    #endregion
}
