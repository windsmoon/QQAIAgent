namespace QQAIAgent.Abstractions.Video;

public interface IVideoCache
{
    #region methods
    /// <summary>按视频 URL 读取完整解析结果，未缓存或已过期时返回 null。</summary>
    /// <param name="cacheDurationSeconds">缓存有效期，单位为秒；为 null 时忽略记录日期。</param>
    Task<string> TryGetAsync(string url, long? cacheDurationSeconds, CancellationToken cancellationToken);
    Task SaveAsync(string url, string resultJson, CancellationToken cancellationToken);
    #endregion
}
