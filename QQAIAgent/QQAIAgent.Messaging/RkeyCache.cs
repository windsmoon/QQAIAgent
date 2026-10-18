namespace QQAIAgent.Messaging;

public sealed class RkeyCache(IMessageReader messageReader) : IDisposable
{
    #region fields
    private readonly Dictionary<string, (string Key, long ExpiresAt)> _keys = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    #endregion

    #region methods
    /// <summary>按 private/group 获取有效图片凭据；缺失或过期时重新获取并缓存接口返回的全部凭据。</summary>
    public async Task<string> GetAsync(string type, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // 等待中的请求拿到锁后重新检查，复用前一个请求刷新后的结果。
            if (_keys.TryGetValue(type, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return cached.Key;
            }

            var keys = await messageReader.GetRkeysAsync(cancellationToken);
            foreach (var key in keys)
            {
                // 提前 5 秒视为过期，给后续图片下载请求留出时间。
                _keys[key.Type] = (key.Key, key.ExpiresAt - 5);
            }

            if (_keys.TryGetValue(type, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return cached.Key;
            }

            throw new InvalidDataException($"NapCat 未返回有效的 {type} 图片下载凭据。");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
    }
    #endregion
}
