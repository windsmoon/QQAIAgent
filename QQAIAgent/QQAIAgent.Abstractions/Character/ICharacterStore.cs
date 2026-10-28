namespace QQAIAgent.Abstractions.Character;

public interface ICharacterStore
{
    #region methods
    /// <summary>按个人人设、群人设、全局默认人设的顺序，读取助手回复当前用户时采用的第一份人设。</summary>
    Task<string> LoadAsync(long userId, long groupId, CancellationToken cancellationToken);
    /// <summary>分别查询当前用户的个人人设和当前群的共享人设，未设置的内容返回 null；不读取默认人设。</summary>
    Task<(string PersonalContent, string GroupContent)> GetAsync(long userId, long groupId, CancellationToken cancellationToken);
    /// <param name="groupId">群号；有值时按群号保存共享人设，为 null 时按用户 QQ 号保存跨群生效的个人人设。</param>
    Task SaveAsync(long userId, long? groupId, string content, CancellationToken cancellationToken);
    /// <param name="groupId">群号；有值时按群号删除共享人设，为 null 时按用户 QQ 号删除个人人设。</param>
    void Delete(long userId, long? groupId);
    #endregion
}
