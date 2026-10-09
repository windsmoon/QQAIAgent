using QQAIAgent.Agent.Config;
using QQAIAgent.Messaging;

namespace QQAIAgent.Agent;

public class GroupMessageAgent(AgentConfig config, long botAccount, IMessageSender messageSender)
{
    #region methods
    /// <summary>Returns the reply message ID, or null when the incoming message does not need a reply.</summary>
    public async Task<long?> HandleMessageAsync(GroupMessage message, CancellationToken cancellationToken)
    {
        if (message.SenderId == botAccount || config.AllowedGroups.Contains(message.GroupId) == false || message.Text != "123ping")
        {
            return null;
        }

        return await messageSender.SendGroupMessageAsync(message.GroupId, "pong", cancellationToken);
    }
    #endregion
}
