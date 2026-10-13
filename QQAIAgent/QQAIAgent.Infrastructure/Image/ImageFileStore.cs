using QQAIAgent.Infrastructure.Database;

namespace QQAIAgent.Infrastructure.Image;

public sealed class ImageFileStore(IDatabase database)
{
    #region methods
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return database.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS image_files (
                storage_source TEXT NOT NULL,
                image_url TEXT NOT NULL,
                file_id TEXT NOT NULL,
                record_time INTEGER NOT NULL,
                storage_duration_seconds INTEGER NOT NULL,
                PRIMARY KEY (storage_source, image_url)
            );
            """, cancellationToken: cancellationToken);
    }

    public Task<string> FindValidFileIdAsync(string storageSource, string imageUrl, CancellationToken cancellationToken)
    {
        return database.ExecuteScalarAsync<string>("""
            SELECT file_id FROM image_files
            WHERE storage_source = @StorageSource AND image_url = @ImageUrl
                AND record_time + storage_duration_seconds > @CurrentTime;
            """, new { StorageSource = storageSource, ImageUrl = imageUrl, CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, cancellationToken);
    }

    public Task RecordAsync(string storageSource, string imageUrl, string fileId, TimeSpan storageDuration, CancellationToken cancellationToken)
    {
        if (storageDuration < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(storageDuration));
        }

        return database.ExecuteAsync("""
            INSERT INTO image_files (storage_source, image_url, file_id, record_time, storage_duration_seconds)
            VALUES (@StorageSource, @ImageUrl, @FileId, @RecordTime, @StorageDurationSeconds)
            ON CONFLICT(storage_source, image_url) DO UPDATE SET
                file_id = excluded.file_id,
                record_time = excluded.record_time,
                storage_duration_seconds = excluded.storage_duration_seconds;
            """, new
        {
            StorageSource = storageSource,
            ImageUrl = imageUrl,
            FileId = fileId,
            RecordTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            StorageDurationSeconds = (long)storageDuration.TotalSeconds
        }, cancellationToken);
    }
    #endregion
}
