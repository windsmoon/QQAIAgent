using Microsoft.Data.Sqlite;
using QQAIAgent.Infrastructure.Database;
using QQAIAgent.Messaging;
using QQAIAgent.Messaging.Content;

namespace QQAIAgent.Infrastructure.Image;

// TODO 这里写的太不易读了，之后改
/// <summary>关联发送结果与自身消息接收，将回复图片对应的原文保存到独立数据库。</summary>
public sealed class TextToImageStore(string directoryPath, long botAccount)
{
    #region fields
    private IDatabase _database;
    private readonly object _pendingLock = new();
    private readonly Dictionary<Guid, (long GroupId, string Text)> _sendingDict = new();
    // Text 为 null 表示接收先到，否则表示发送结果先到；两边按群号和消息 ID 汇合。
    private readonly Dictionary<(long GroupId, long MessageId), (string Text, string CacheKey, DateTimeOffset ExpiresAt)> _pendingDict = new();
    #endregion

    #region methods
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directoryPath);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directoryPath, "TextToImageCache.sqlite3")
        }.ToString();
        _database = new SqliteDatabase(connectionString);
        await _database.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS text_to_image (
                cache_key TEXT NOT NULL PRIMARY KEY,
                reply_text TEXT NOT NULL
            );
            """, cancellationToken: cancellationToken);
    }

    /// <summary>发送前保留原文；此时还没有消息 ID，使用本次发送的临时标识。</summary>
    public Guid BeginSend(long groupId, string text)
    {
        lock (_pendingLock)
        {
            RemoveExpiredPending();
            var sendId = Guid.NewGuid();
            _sendingDict.Add(sendId, (groupId, text));
            return sendId;
        }
    }

    public void CancelSend(Guid sendId)
    {
        lock (_pendingLock)
        {
            _sendingDict.Remove(sendId);
        }
    }

    /// <summary>发送成功后用群号和返回的消息 ID 匹配接，兼容接收先到的情况。</summary>
    public Task CompleteSendAsync(Guid sendId, long messageId, CancellationToken cancellationToken)
    {
        string text;
        string cacheKey;
        // 发送接口响应和自身消息接收可能由不同线程处理，而两个流程都会访问这些字典。
        lock (_pendingLock)
        {
            RemoveExpiredPending();
            // 发送成功了，可以移除。如果不在这里，说明已经处理过了。
            if (_sendingDict.Remove(sendId, out var sent) == false)
            {
                return Task.CompletedTask;
            }

            var key = (sent.GroupId, messageId);
            // 这是处理发送结果先于接消息收到的情况
            // 在 _pendingDict 中没找到对应项，说明接收流程还没有把这条自身消息的信息放进来。
            if (_pendingDict.Remove(key, out var received) == false)
            {
                _pendingDict[key] = (sent.Text, "", DateTimeOffset.UtcNow.AddMinutes(5));
                return Task.CompletedTask;
            }

            text = sent.Text;
            cacheKey = received.CacheKey;
        }

        return SaveAsync(cacheKey, text, cancellationToken);
    }

    public Task RecordReceivedAsync(GroupMessage message, CancellationToken cancellationToken)
    {
        if (message.SenderId != botAccount || message.MessageId is not long messageId)
        {
            return Task.CompletedTask;
        }

        // 当前一条回复只对应一张图片；纯文本上报也参与匹配，以便及时移除待记录文本。
        var cacheKey = message.MessageContentList.OfType<ImageContent>().FirstOrDefault()?.CacheKey ?? "";
        string text;
        lock (_pendingLock)
        {
            RemoveExpiredPending();
            var key = (message.GroupId, messageId);
            // 表示发送结果已经在里面了
            if (_pendingDict.TryGetValue(key, out var pending) && pending.Text is not null)
            {
                _pendingDict.Remove(key);
                text = pending.Text;
            }
            else
            {
                // 接收先到
                if (_sendingDict.Values.Any(sent => sent.GroupId == message.GroupId))
                {
                    _pendingDict[key] = (null, cacheKey, DateTimeOffset.UtcNow.AddMinutes(5));
                }

                return Task.CompletedTask;
            }
        }

        return SaveAsync(cacheKey, text, cancellationToken);
    }

    /// <summary>在消息解析完成后还原秘书的回复图片，未命中的图片保持原样，原始正文不变。</summary>
    public async Task RestoreAsync(GroupMessage message, CancellationToken cancellationToken)
    {
        for (var index = 0; index < message.MessageContentList.Count; index++)
        {
            var content = message.MessageContentList[index];
            if (content is ImageContent image && message.SenderId == botAccount && image.CacheKey != "")
            {
                var text = await _database.ExecuteScalarAsync<string>("""
                    SELECT reply_text FROM text_to_image WHERE cache_key = @CacheKey;
                    """, new { image.CacheKey }, cancellationToken);
                if (text is not null)
                {
                    // 原位替换，不重新编号，后面的图片继续使用解析时生成的 CacheKey。
                    message.MessageContentList[index] = new TextContent { Text = text };
                }
            }
            else if (content is ForwardContent forward)
            {
                foreach (var innerMessage in forward.InnerMessages)
                {
                    await RestoreAsync(innerMessage, cancellationToken);
                }
            }
        }
    }

    private Task SaveAsync(string cacheKey, string text, CancellationToken cancellationToken)
    {
        if (cacheKey == "")
        {
            return Task.CompletedTask;
        }

        return _database.ExecuteAsync("""
            INSERT INTO text_to_image (cache_key, reply_text)
            VALUES (@CacheKey, @Text)
            ON CONFLICT(cache_key) DO UPDATE SET reply_text = excluded.reply_text;
            """, new { CacheKey = cacheKey, Text = text }, cancellationToken);
    }

    // 上报丢失或发送失败留下的未匹配项只保留五分钟，避免长期积累回复原文和图片键。
    // 调用方持有 _pendingLock。
    private void RemoveExpiredPending()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _pendingDict.Where(item => item.Value.ExpiresAt <= now).Select(item => item.Key).ToList())
        {
            _pendingDict.Remove(key);
        }
    }
    #endregion
}
