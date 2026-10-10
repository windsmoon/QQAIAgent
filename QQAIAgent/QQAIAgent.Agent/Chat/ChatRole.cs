namespace QQAIAgent.Agent.Chat;

public enum ChatRole
{
    System = 0, // 系统指令
    User = 1, // 用户输入
    Assistant = 2, // 模型输出
    Tool = 3 // 工具结果
}
