namespace QQAIAgent.Abstractions.Messages;

public interface IGroupMessageQuery
{
    Task<GroupMessageSearchResult> SearchAsync(long groupId, GroupMessageFilter filter, int limit, int offset,
        bool oldestFirst, CancellationToken cancellationToken);

    Task<long> CountAsync(long groupId, GroupMessageFilter filter, CancellationToken cancellationToken);
}
