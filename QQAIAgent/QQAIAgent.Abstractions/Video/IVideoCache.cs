namespace QQAIAgent.Abstractions.Video;

public interface IVideoCache
{
    #region methods
    /// <summary>按“群号-原消息ID-视频索引”读取完整解析结果，未缓存或已过期时返回 null。</summary>
    /// <param name="cacheDurationSeconds">缓存有效期，单位为秒；为 null 时忽略记录日期。</param>
    Task<string> TryGetAsync(string cacheKey, long? cacheDurationSeconds, CancellationToken cancellationToken);
    Task SaveAsync(string cacheKey, string resultJson, CancellationToken cancellationToken);
    #endregion
}
