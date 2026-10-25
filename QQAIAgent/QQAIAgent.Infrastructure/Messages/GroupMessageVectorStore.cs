using Microsoft.Data.Sqlite;
using QQAIAgent.Infrastructure.Database;

namespace QQAIAgent.Infrastructure.Messages;

/// <summary>在已有群消息数据库中维护向量表，通过本地记录主键关联原消息。</summary>
public sealed class GroupMessageVectorStore
{
    #region fields
    private readonly IDatabase _database;
    #endregion

    #region constructors
    public GroupMessageVectorStore(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWrite
        }.ToString();
        _database = new SqliteDatabase(connectionString);
    }
    #endregion

    #region methods
    /// <summary>建立向量表，保留已有向量，不自动生成历史消息向量。</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return _database.ExecuteAsync("""
            CREATE VIRTUAL TABLE IF NOT EXISTS messages_vec USING vec0(
                record_id INTEGER PRIMARY KEY,
                embedding FLOAT[384] distance_metric=cosine
            );
            """, cancellationToken: cancellationToken);
    }
    #endregion
}
