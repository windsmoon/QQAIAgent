using QQAIAgent.Agent.Chat;

namespace QQAIAgent.Agent;

public interface IChatModel
{
    #region properties
    bool SupportsToolCalling { get; }
    #endregion

    #region methods
    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken);
    #endregion
}
