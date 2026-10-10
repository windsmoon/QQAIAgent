using QQAIAgent.Agent.Chat;
using QQAIAgent.Agent.Config;
using QQAIAgent.Messaging;

namespace QQAIAgent.Agent;

public class GroupMessageAgent(AgentConfig config, long botAccount, IMessageSender messageSender, IChatModel chatModel, IReadOnlyList<ChatToolDefinition> toolDefinitions)
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

        ChatResponse response;
        try
        {
            response = await chatModel.CompleteAsync(new ChatRequest
            {
                Messages = [new ChatMessage { Role = ChatRole.User, Content = text }],
                Tools = chatModel.SupportsToolCalling ? toolDefinitions : []
            }, cancellationToken);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
        {
            var failureText = exception is OperationCanceledException
                ? "模型请求超时，请稍后再试"
                : "模型请求失败，请稍后再试";
            await messageSender.SendGroupMessageAsync(message.GroupId, failureText, cancellationToken);
            // 提示发送后继续抛出原异常，由宿主记录日志并处理下一条消息。
            throw;
        }

        if (string.IsNullOrWhiteSpace(response.Message.Content))
        {
            return await messageSender.SendGroupMessageAsync(message.GroupId, "回复内容为空", cancellationToken);
        }

        return await messageSender.SendGroupMessageAsync(message.GroupId, response.Message.Content, cancellationToken);
    }
    #endregion
}
