namespace QQAIAgent.Abstractions;

/// <summary>模型无关的相对思考强度，由具体模型类库映射到实际请求参数。</summary>
public enum ReasoningEffort
{
    Minimal,
    Low,
    Medium,
    High,
    Max
}
