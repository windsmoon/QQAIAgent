using System.Globalization;
using System.Text.Json;

namespace QQAIAgent.Infrastructure.Runtime;

/// <summary>首次访问时读取对应群的 JSON，按群复用运行时数据；修改后按群保存。</summary>
public sealed class RuntimeDataStore(string directoryPath)
{
    #region fields
    private readonly Dictionary<long, RuntimeData> _groupDict = [];
    #endregion

    #region methods
    public RuntimeData Get(long groupId)
    {
        lock (_groupDict)
        {
            if (_groupDict.TryGetValue(groupId, out var data) == false)
            {
                var path = GetPath(groupId);
                data = File.Exists(path)
                    ? JsonSerializer.Deserialize<RuntimeData>(File.ReadAllText(path)) ?? new RuntimeData()
                    : new RuntimeData();
                _groupDict.Add(groupId, data);
            }

            return data;
        }
    }

    public void Save(long groupId)
    {
        lock (_groupDict)
        {
            var data = Get(groupId);
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(GetPath(groupId), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private string GetPath(long groupId)
    {
        return Path.Combine(directoryPath, $"{groupId.ToString(CultureInfo.InvariantCulture)}.json");
    }
    #endregion
}
