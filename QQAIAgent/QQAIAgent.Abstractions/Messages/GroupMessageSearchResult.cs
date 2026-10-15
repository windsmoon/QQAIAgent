namespace QQAIAgent.Abstractions.Messages;

public sealed class GroupMessageSearchResult
{
    public List<GroupMessageSearchItem> Messages { get; set; } = [];
    public bool HasMore { get; set; }
}
