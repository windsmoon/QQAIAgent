namespace QQAIAgent.Abstractions.Character;

public interface ICharacterStore
{
    #region methods
    /// <summary>按群内、全局、默认的顺序读取当前用户的第一份人设。</summary>
    Task<string> LoadAsync(long userId, long groupId, CancellationToken cancellationToken);
    /// <summary>分别查询当前群内和全局人设，未设置的内容返回 null；不读取默认人设。</summary>
    Task<(string GroupContent, string GlobalContent)> GetAsync(long userId, long groupId, CancellationToken cancellationToken);
    /// <param name="groupId">群号；为 null 时保存全局人设。</param>
    Task SaveAsync(long userId, long? groupId, string content, CancellationToken cancellationToken);
    /// <param name="groupId">群号；为 null 时删除全局人设。</param>
    void Delete(long userId, long? groupId);
    #endregion
}
