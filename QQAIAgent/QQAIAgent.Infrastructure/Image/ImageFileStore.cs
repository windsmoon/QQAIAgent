using Microsoft.Data.Sqlite;
using QQAIAgent.Infrastructure.Database;

namespace QQAIAgent.Infrastructure.Image;

public sealed class ImageFileStore(string directoryPath)
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
            DataSource = Path.Combine(directoryPath, "ImageMessageCache.sqlite3")
        }.ToString();
        _database = new SqliteDatabase(connectionString);
        await _database.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS image_files (
                storage_source TEXT NOT NULL,
                cache_key TEXT NOT NULL,
                file_id TEXT NOT NULL,
                record_time INTEGER NOT NULL,
                storage_duration_seconds INTEGER NOT NULL,
                PRIMARY KEY (storage_source, cache_key)
            );
            """, cancellationToken: cancellationToken);
    }

    public Task<string> FindValidFileIdAsync(string storageSource, string cacheKey, CancellationToken cancellationToken)
    {
        return _database.ExecuteScalarAsync<string>("""
            SELECT file_id FROM image_files
            WHERE storage_source = @StorageSource AND cache_key = @CacheKey
                AND record_time + storage_duration_seconds > @CurrentTime;
            """, new { StorageSource = storageSource, CacheKey = cacheKey, CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, cancellationToken);
    }

    public Task RecordAsync(string storageSource, string cacheKey, string fileId, TimeSpan storageDuration, CancellationToken cancellationToken)
    {
        if (storageDuration < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(storageDuration));
        }

        return _database.ExecuteAsync("""
            INSERT INTO image_files (storage_source, cache_key, file_id, record_time, storage_duration_seconds)
            VALUES (@StorageSource, @CacheKey, @FileId, @RecordTime, @StorageDurationSeconds)
            ON CONFLICT(storage_source, cache_key) DO UPDATE SET
                file_id = excluded.file_id,
                record_time = excluded.record_time,
                storage_duration_seconds = excluded.storage_duration_seconds;
            """, new
        {
            StorageSource = storageSource,
            CacheKey = cacheKey,
            FileId = fileId,
            RecordTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            StorageDurationSeconds = (long)storageDuration.TotalSeconds
        }, cancellationToken);
    }
    #endregion
}
