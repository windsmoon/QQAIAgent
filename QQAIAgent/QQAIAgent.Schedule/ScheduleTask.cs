namespace QQAIAgent.Schedule;

public class ScheduleTask
{
    #region properties
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<long> GroupIds { get; set; } = [];
    public ScheduleType ScheduleType { get; set; }
    /// <summary>全局固定 UTC 偏移下的执行时间，使用 24 小时制 HH:mm。</summary>
    public string Time { get; set; } = string.Empty;
    /// <summary>单次任务的日期，格式为 yyyy-MM-dd。</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>每周任务执行的星期；读取 JSON 时需注册 JsonStringEnumConverter。</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];
    /// <summary>传给业务回调的消息内容，可省略；调度器不负责发送消息。</summary>
    public string Text { get; set; } = string.Empty;
    #endregion
}
