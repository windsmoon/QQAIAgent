namespace QQAIAgent.Schedule;

public class ScheduleConfig
{
    #region properties
    public bool Enabled { get; set; } = true;
    /// <summary>固定 UTC 偏移，例如 +8、-5、+5:30，不使用系统时区或夏令时规则。</summary>
    public string TimeZone { get; set; } = "+8";
    public List<ScheduleTask> Tasks { get; set; } = [];
    #endregion
}
