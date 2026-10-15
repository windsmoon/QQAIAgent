using System.Text.Json.Serialization;

namespace QQAIAgent.Schedule;

[JsonConverter(typeof(JsonStringEnumConverter<ScheduleType>))]
public enum ScheduleType
{
    Once,
    Daily,
    Weekly
}
