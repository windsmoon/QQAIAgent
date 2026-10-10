using QQAIAgent.Agent.Chat;
using QQAIAgent.Agent.Config;
using QQAIAgent.Messaging;

namespace QQAIAgent.Agent;

public class GroupMessageAgent(AgentConfig config, long botAccount, IMessageSender messageSender, IChatModel chatModel)
{
    #region fields
    #endregion
    
    #region methods
    /// <summary>处理群消息并回复原群，返回回复消息 ID；空输入或空回复会发送对应提示。</summary>
    public async Task<long?> HandleMessageAsync(GroupMessage message, CancellationToken cancellationToken)
    {
        // 消息接收端已筛选出需要处理的消息，并匹配了 "123" 前缀。
        var text = message.Text.TrimStart()[3..].Trim();
        if (text.Length == 0)
        {
            return await messageSender.SendGroupMessageAsync(message.GroupId, "输入内容为空", cancellationToken);
        }

        var response = await chatModel.CompleteAsync(new ChatRequest
        {
            Messages = [new ChatMessage { Role = ChatRole.User, Content = text }]
        }, cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Message.Content))
        {
            return await messageSender.SendGroupMessageAsync(message.GroupId, "回复内容为空", cancellationToken);
        }

        return await messageSender.SendGroupMessageAsync(message.GroupId, response.Message.Content, cancellationToken);
    }
    #endregion
}
