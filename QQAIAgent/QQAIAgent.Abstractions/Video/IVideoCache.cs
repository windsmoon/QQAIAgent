namespace QQAIAgent.Abstractions.Video;

public interface IVideoCache
{
    #region methods
    /// <summary>按视频 URL 读取完整解析结果，未缓存时返回 null。</summary>
    Task<string> TryGetAsync(string url, CancellationToken cancellationToken);
    Task SaveAsync(string url, string resultJson, CancellationToken cancellationToken);
    #endregion
}
