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
            DataSource = Path.Combine(directoryPath, "VideoMessageCache.sqlite3")
        }.ToString();
        _database = new SqliteDatabase(connectionString);
        await _database.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS video_cache (
                cache_key TEXT NOT NULL PRIMARY KEY,
                result_json TEXT NOT NULL,
                record_time INTEGER NOT NULL
            );
            """, cancellationToken: cancellationToken);
    }

    public Task<string> TryGetAsync(string cacheKey, long? cacheDurationSeconds, CancellationToken cancellationToken)
    {
        return _database.ExecuteScalarAsync<string>("""
            SELECT result_json FROM video_cache
            WHERE cache_key = @CacheKey
                AND (@CacheDurationSeconds IS NULL OR record_time + @CacheDurationSeconds > @CurrentTime);
            """, new
        {
            CacheKey = cacheKey,
            CacheDurationSeconds = cacheDurationSeconds,
            CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }, cancellationToken);
    }

    public Task SaveAsync(string cacheKey, string resultJson, CancellationToken cancellationToken)
    {
        return _database.ExecuteAsync("""
            INSERT INTO video_cache (cache_key, result_json, record_time)
            VALUES (@CacheKey, @ResultJson, @RecordTime)
            ON CONFLICT(cache_key) DO UPDATE SET
                result_json = excluded.result_json,
                record_time = excluded.record_time;
            """, new
        {
            CacheKey = cacheKey,
            ResultJson = resultJson,
            // 使用 UTC Unix 秒记录本次写入时间，用于判断缓存是否过期。
            RecordTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }, cancellationToken);
    }
    #endregion
}
