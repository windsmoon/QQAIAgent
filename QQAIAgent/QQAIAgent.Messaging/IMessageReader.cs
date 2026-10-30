namespace QQAIAgent.Messaging;

public interface IMessageReader
{
    #region methods
    /// <summary>按消息 ID 获取群消息，保留正文消息段和引用关系。</summary>
    Task<GroupMessage> GetGroupMessageAsync(long messageId, CancellationToken cancellationToken);
    /// <summary>按资源 ID 获取合并转发内的消息列表，本地缓存读写不受聊天记录开关限制。</summary>
    Task<IReadOnlyList<GroupMessage>> GetForwardMessagesAsync(string resId, CancellationToken cancellationToken);
    /// <summary>根据群号和群文件资源 ID 获取下载地址。</summary>
    Task<string> GetGroupFileUrlAsync(long groupId, string fileId, CancellationToken cancellationToken);
    /// <summary>获取当前图片下载凭据；ExpiresAt 为创建时间加有效期得到的 Unix 秒时间戳。</summary>
    Task<List<(string Type, string Key, long ExpiresAt)>> GetRkeysAsync(CancellationToken cancellationToken);
    Task<string> GetGroupMemberCardAsync(long groupId, long userId, CancellationToken cancellationToken);
    /// <summary>使用接口默认缓存查询群成员角色，返回 owner、admin 或 member。</summary>
    Task<string> GetGroupMemberRoleAsync(long groupId, long userId, CancellationToken cancellationToken);
    /// <summary>根据 QQ 号获取用户头像图片地址，不验证账号是否存在。</summary>
    Task<string> GetUserAvatarUrlAsync(long userId, CancellationToken cancellationToken);
    #endregion
}
