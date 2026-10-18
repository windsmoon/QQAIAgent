namespace QQAIAgent.Schedule;

public class ScheduleTask
{
    #region properties
    public string Id { get; set; } = string.Empty;
    /// <summary>任务实现的名称，默认使用内置群文本消息处理器。</summary>
    public string FunctionName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>消息发送目标群；仅包含一个 -1 时表示 AgentConfig.AllowedGroups 中的所有群，空列表不发送。</summary>
    public List<long> GroupIds { get; set; } = [];
    /// <summary>从发送目标中排除的群号，适用于指定群和所有群两种配置。</summary>
    public List<long> ExcludedGroupIds { get; set; } = [];
    public ScheduleType ScheduleType { get; set; }
    /// <summary>全局固定 UTC 偏移下的执行时间，使用 24 小时制 HH:mm。</summary>
    public string Time { get; set; } = string.Empty;
    /// <summary>单次任务的日期，格式为 yyyy-MM-dd。</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>每周任务执行的星期；读取 JSON 时需注册 JsonStringEnumConverter。</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];
    /// <summary>传给业务回调的文本；发送消息任务用作消息正文，总结任务用作提示词；调度器不解释内容。</summary>
    public string Text { get; set; } = string.Empty;
    #endregion
}
