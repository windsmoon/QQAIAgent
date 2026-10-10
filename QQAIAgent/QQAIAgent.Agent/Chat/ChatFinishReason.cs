namespace QQAIAgent.Agent.Chat;

public enum ChatFinishReason
{
    Unknown = 0,
    Finish = 1, // 正常完成
    Length = 2, // 达到输出上限
    ToolCalls = 3, // 请求工具调用
    ContentFilter = 4, // 内容被模型安全过滤 
    Interrupted = 5 // 中断
}
