using QQAIAgent.Agent.Config;
using QQAIAgent.Messaging;

namespace QQAIAgent.Agent;

public class GroupMessageAgent(AgentConfig config, long botAccount, IMessageSender messageSender)
{
    #region fields
    #endregion
    
    #region methods
    /// <summary>Returns the reply message ID, or null when the incoming message does not need a reply.</summary>
    public async Task<long?> HandleMessageAsync(GroupMessage message, CancellationToken cancellationToken)
    {
        // 走到这肯定是合法的需要处理的消息了
        return await messageSender.SendGroupMessageAsync(message.GroupId, "pong", cancellationToken);
    }
    #endregion
}
