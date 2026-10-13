namespace QQAIAgent.Messaging;

public interface IMessageReader
{
    #region methods
    /// <summary>按消息 ID 获取群消息，保留正文消息段和引用关系。</summary>
    Task<GroupMessage> GetGroupMessageAsync(long messageId, CancellationToken cancellationToken);
    /// <summary>按资源 ID 获取合并转发内的消息列表。</summary>
    Task<IReadOnlyList<GroupMessage>> GetForwardMessagesAsync(string resId, CancellationToken cancellationToken);
    /// <summary>根据群号和群文件资源 ID 获取下载地址。</summary>
    Task<string> GetGroupFileUrlAsync(long groupId, string fileId, CancellationToken cancellationToken);
    #endregion
}
