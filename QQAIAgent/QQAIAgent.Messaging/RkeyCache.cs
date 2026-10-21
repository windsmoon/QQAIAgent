namespace QQAIAgent.Messaging;

public sealed class RkeyCache(IMessageReader messageReader) : IDisposable
{
    #region fields
    private readonly Dictionary<string, (string Key, long ExpiresAt)> _keys = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    #endregion

    #region methods
    /// <summary>获取图片下载链接，按图片业务类型替换链接中已有的 rkey，其他链接保持原样。</summary>
    public async Task<string> GetImageDownloadUrlAsync(string imageUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);

        var imageUri = new Uri(imageUrl);
        if (imageUri.Host == "multimedia.nt.qq.com.cn")
        {
            var queryParts = imageUri.Query.TrimStart('?').Split('&').ToList();
            // 仅处理图片的两种业务类型，不能把视频等其他 appid 当作群聊图片。
            var keyType = queryParts.Contains("appid=1406") ? "private"
                : queryParts.Contains("appid=1407") ? "group" : string.Empty;
            var rkeyIndex = queryParts.FindIndex(part => part.StartsWith("rkey=", StringComparison.Ordinal));
            if (keyType != string.Empty && rkeyIndex >= 0)
            {
                var rkey = await GetAsync(keyType, cancellationToken);
                // 下载前替换有效凭据，保留 fileid 等原始参数；上传缓存仍使用消息来源键。
                queryParts[rkeyIndex] = "rkey=" + Uri.EscapeDataString(rkey);
                imageUrl = new UriBuilder(imageUri) { Query = string.Join("&", queryParts) }.Uri.AbsoluteUri;
            }
        }

        return imageUrl;
    }

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
