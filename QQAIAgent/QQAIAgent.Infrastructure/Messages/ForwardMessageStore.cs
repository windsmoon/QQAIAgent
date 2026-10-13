using Microsoft.Data.Sqlite;
using QQAIAgent.Infrastructure.Database;

namespace QQAIAgent.Infrastructure.Messages;

/// <summary>合并转发消息缓存，资源 ID 为主键，命中后不再调用 get_forward_msg。Nullable 在本类库关闭，未命中以 null 返回。</summary>
public sealed class ForwardMessageStore(string directoryPath)
{
    #region fields
    private IDatabase _database;
    #endregion

    #region methods
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directoryPath);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directoryPath, "forward.sqlite3")
        }.ToString();
        _database = new SqliteDatabase(connectionString);
        await _database.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS forward_messages (
                resource_id TEXT PRIMARY KEY,
                data_json TEXT NOT NULL
            );
            """, cancellationToken: cancellationToken);
    }

    /// <summary>按资源 ID 读取缓存的 get_forward_msg 响应 data 原文，未缓存时返回 null。</summary>
    public async Task<string> TryGetAsync(string resourceId, CancellationToken cancellationToken)
    {
        var database = _database ?? throw new InvalidOperationException("ForwardMessageStore 尚未初始化。");
        var results = await database.QueryAsync<string>(
            "SELECT data_json FROM forward_messages WHERE resource_id = @ResourceId;",
            new { ResourceId = resourceId }, cancellationToken);
        return results.FirstOrDefault();
    }

    /// <summary>保存 get_forward_msg 响应的 data 原文，同资源 ID 覆盖旧值。</summary>
    public Task SaveAsync(string resourceId, string dataJson, CancellationToken cancellationToken)
    {
        var database = _database ?? throw new InvalidOperationException("ForwardMessageStore 尚未初始化。");
        return database.ExecuteAsync("""
            INSERT OR REPLACE INTO forward_messages (resource_id, data_json)
            VALUES (@ResourceId, @DataJson);
            """, new { ResourceId = resourceId, DataJson = dataJson }, cancellationToken);
    }
    #endregion
}
