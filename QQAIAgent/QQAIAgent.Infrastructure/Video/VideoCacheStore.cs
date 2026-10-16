using Microsoft.Data.Sqlite;
using QQAIAgent.Abstractions.Video;
using QQAIAgent.Infrastructure.Database;

namespace QQAIAgent.Infrastructure.Video;

public sealed class VideoCacheStore(string directoryPath) : IVideoCache
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
            DataSource = Path.Combine(directoryPath, "VideoCache.sqlite3")
        }.ToString();
        _database = new SqliteDatabase(connectionString);
        await _database.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS video_cache (
                url TEXT NOT NULL PRIMARY KEY,
                result_json TEXT NOT NULL
            );
            """, cancellationToken: cancellationToken);
    }

    public Task<string> TryGetAsync(string url, CancellationToken cancellationToken)
    {
        return _database.ExecuteScalarAsync<string>(
            "SELECT result_json FROM video_cache WHERE url = @Url;",
            new { Url = url }, cancellationToken);
    }

    public Task SaveAsync(string url, string resultJson, CancellationToken cancellationToken)
    {
        return _database.ExecuteAsync("""
            INSERT INTO video_cache (url, result_json)
            VALUES (@Url, @ResultJson)
            ON CONFLICT(url) DO UPDATE SET result_json = excluded.result_json;
            """, new { Url = url, ResultJson = resultJson }, cancellationToken);
    }
    #endregion
}
