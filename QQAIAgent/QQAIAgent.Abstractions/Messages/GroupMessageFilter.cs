#nullable disable

namespace QQAIAgent.Abstractions.Messages;

public sealed class GroupMessageFilter
{
    #region properties
    public string Keyword { get; init; }
    public long? SenderId { get; init; }
    public string SenderName { get; init; }
    public long? StartTime { get; init; }
    public long? EndTime { get; init; }
    public long? MessageId { get; init; }
    public long? RefMessageId { get; init; }
    #endregion

    #region methods
    public void Validate()
    {
        if (Keyword is not null && (string.IsNullOrWhiteSpace(Keyword) || Keyword.Length > 200))
        {
            throw new ArgumentException("keyword 必须为 1 到 200 个字符的非空白文本。");
        }

        if (SenderName is not null && (string.IsNullOrWhiteSpace(SenderName) || SenderName.Length > 100))
        {
            throw new ArgumentException("sender_name 必须为 1 到 100 个字符的非空白文本。");
        }

        if (SenderId is <= 0)
        {
            throw new ArgumentException("sender_id 必须是正整数 QQ 号。");
        }

        if (StartTime is < 0 || EndTime is < 0 || StartTime > EndTime)
        {
            throw new ArgumentException("start_time 和 end_time 必须是非负 Unix 秒，且开始时间不能晚于结束时间。");
        }
    }
    #endregion
}
