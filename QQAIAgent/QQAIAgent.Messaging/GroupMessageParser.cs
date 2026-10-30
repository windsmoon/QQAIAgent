using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using QQAIAgent.Messaging.Content;

namespace QQAIAgent.Messaging;

public static class GroupMessageParser
{
    #region methods
    // 群消息事件与 get_msg 响应共用内容解析；API 响应不依赖事件的 post_type。
    public static GroupMessage ParseGroupMessage(JsonElement root)
    {
        var message = root.GetProperty("message");
        root.TryGetProperty("sender", out var sender);
        var groupMessage = new GroupMessage
        {
            // 事件与 get_msg 响应都带消息 ID，用于回复时引用提问消息。
            MessageId = root.TryGetProperty("message_id", out var messageId) && messageId.ValueKind != JsonValueKind.Null
                ? ReadMessageInteger(messageId)
                : null,
            MessageTime = root.TryGetProperty("time", out var time) && time.ValueKind != JsonValueKind.Null
                ? ReadMessageInteger(time)
                : null,
            GroupId = root.TryGetProperty("group_id", out var groupId) ? ReadMessageInteger(groupId) : 0,
            SenderId = root.TryGetProperty("user_id", out var userId) ? ReadMessageInteger(userId) : 0,
            SenderNickname = sender.ValueKind == JsonValueKind.Object && sender.TryGetProperty("nickname", out var nickname)
                ? nickname.GetString() ?? ""
                : "",
            SenderCard = sender.ValueKind == JsonValueKind.Object && sender.TryGetProperty("card", out var card)
                ? card.GetString() ?? ""
                : "",
            SubType = root.TryGetProperty("sub_type", out var subType) ? subType.GetString() ?? "" : "",
            ContentJson = message.GetRawText()
        };
        ParseMessageContents(groupMessage, message);

        return groupMessage;
    }

    /// <summary>解析消息段数组或 CQ 字符串，将正文和引用关系写入 GroupMessage。</summary>
    public static void ParseMessageContents(GroupMessage groupMessage, JsonElement message)
    {
        // napcat 配置决定是发来原始文本消息还是已经解析好的数组
        if (message.ValueKind == JsonValueKind.String)
        {
            ParseCqMessage(message.GetString() ?? "", groupMessage);
        }
        else if (message.ValueKind == JsonValueKind.Array)
        {
            foreach (var segment in message.EnumerateArray())
            {
                AppendMessageContent(groupMessage, segment.GetProperty("type").GetString() ?? "", segment.GetProperty("data"));
            }
        }
        else
        {
            throw new InvalidDataException("NapCat 群消息内容必须是字符串或消息段数组。");
        }
    }

    private static void AppendMessageContent(GroupMessage message, string type, JsonElement data)
    {
        switch (type)
        {
            case "text":
                message.MessageContentList.Add(new TextContent
                {
                    Text = data.GetProperty("text").GetString() ?? ""
                });
                break;
            case "at":
                message.MessageContentList.Add(new AtContent
                {
                    // qq 可以是数字或字符串，"all" 表示全体成员。
                    Target = data.GetProperty("qq").ToString()
                });
                break;
            case "face":
                {
                    var faceId = ReadMessageInteger(data.GetProperty("id"));
                    message.MessageContentList.Add(new FaceContent
                    {
                        Id = faceId,
                        // 未知 ID 保留空名称，由处理端降级显示。
                        Name = QqFaceTable.GetName(faceId)
                    });
                }
                break;
            case "mface":
                {
                    // 商城表情本质是表情包图片，按图片处理；summary 是 QQ 官方的表情描述文字。
                    message.MessageContentList.Add(new ImageContent
                    {
                        SourceGroupId = message.GroupId,
                        SourceMessageId = message.MessageId,
                        ImageIndex = message.MessageContentList.OfType<ImageContent>().Count(),
                        CacheKey = message.GroupId != 0 && message.MessageId.HasValue
                            ? $"{message.GroupId}-{message.MessageId.Value}-{message.MessageContentList.Count}"
                            : string.Empty,
                        Url = data.TryGetProperty("url", out var mfaceUrl) ? mfaceUrl.GetString() ?? "" : "",
                        Summary = data.TryGetProperty("summary", out var mfaceSummary) ? mfaceSummary.GetString() ?? "" : ""
                    });
                }
                break;
            case "json":
                message.MessageContentList.Add(ParseJsonCard(data));
                break;
            case "forward":
                {
                    // 合并转发内容需在处理阶段展开，这里保留资源 ID；内层转发的节点内容会随 data.content 内联返回。
                    var forwardContent = new ForwardContent { ResourceId = data.GetProperty("id").ToString() };
                    if (data.TryGetProperty("content", out var innerNodes) && innerNodes.ValueKind == JsonValueKind.Array)
                    {
                        // 内层节点结构与群消息事件同构，直接按群消息解析，避免再调接口（内层 ID 会返回 retcode=1200）。
                        forwardContent.InnerMessages.AddRange(innerNodes.EnumerateArray().Select(ParseGroupMessage));
                    }

                    message.MessageContentList.Add(forwardContent);
                }
                break;
            case "image":
                message.MessageContentList.Add(new ImageContent
                {
                    SourceGroupId = message.GroupId,
                    SourceMessageId = message.MessageId,
                    ImageIndex = message.MessageContentList.OfType<ImageContent>().Count(),
                    CacheKey = message.GroupId != 0 && message.MessageId.HasValue
                        ? $"{message.GroupId}-{message.MessageId.Value}-{message.MessageContentList.Count}"
                        : string.Empty,
                    File = data.GetProperty("file").GetString() ?? "",
                    Url = data.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
                    Summary = data.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "" : "",
                    SubType = data.TryGetProperty("sub_type", out var subType) && subType.ValueKind != JsonValueKind.Null
                        ? (ImageSubType)ReadMessageInteger(subType)
                        : ImageSubType.Normal,
                    FileSize = data.TryGetProperty("file_size", out var fileSize) && fileSize.ValueKind != JsonValueKind.Null
                        ? ReadMessageInteger(fileSize)
                        : 0
                });
                break;
            case "video":
            case "file" when Path.GetExtension(ReadOptionalString(data, "file")).ToLowerInvariant() is
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".flv" or ".wmv":
                message.MessageContentList.Add(new VideoContent
                {
                    File = data.GetProperty("file").GetString() ?? "",
                    FileId = type == "file" ? ReadOptionalString(data, "file_id") : "",
                    SourceGroupId = message.GroupId,
                    SourceMessageId = message.MessageId,
                    VideoIndex = message.MessageContentList.OfType<VideoContent>().Count(),
                    Url = ReadOptionalString(data, "url"),
                    FileSize = data.TryGetProperty("file_size", out var videoFileSize) && videoFileSize.ValueKind != JsonValueKind.Null
                        ? ReadMessageInteger(videoFileSize)
                        : 0
                });
                break;
            case "reply":
                // 引用是上下文关系，不加入当前用户的正文消息段。
                message.RefMessageId = ReadMessageInteger(data.GetProperty("id"));
                break;
            default:
                message.MessageContentList.Add(new UnknownContent
                {
                    OriginalType = type,
                    DataJson = data.GetRawText()
                });
                break;
        }
    }

    // json 卡段的 data 字段是再编码一层的 JSON 字符串，常见结构为 prompt 摘要加 meta 下首个子对象的 title/desc/qqdocurl。
    private static JsonCardContent ParseJsonCard(JsonElement data)
    {
        var card = new JsonCardContent();
        JsonElement root;
        try
        {
            root = data.ValueKind == JsonValueKind.String
                ? JsonSerializer.Deserialize<JsonElement>(data.GetString() ?? "")
                : data;
        }
        catch (JsonException)
        {
            // 内层 JSON 无效时保留空卡片，由处理端降级显示。
            return card;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return card;
        }

        card.Prompt = ReadOptionalString(root, "prompt");
        if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            // TODO
            // 卡片类型不同，meta 下的子对象名也不同（detail_1/detail_2/news 等），取首个对象。
            foreach (var detail in meta.EnumerateObject())
            {
                if (detail.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                card.Title = ReadOptionalString(detail.Value, "title");
                card.Description = ReadOptionalString(detail.Value, "desc");
                card.Url = ReadOptionalString(detail.Value, "qqdocurl");
                break;
            }
        }

        return card;
    }

    private static string ReadOptionalString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static void ParseCqMessage(string text, GroupMessage message)
    {
        var position = 0;
        // 先识别未转义的 CQ 码，再解码文本，避免把用户输入的字面 CQ 码当成消息段。
        foreach (Match match in Regex.Matches(text, @"\[CQ:(?<type>[a-zA-Z0-9_]+)(?<data>(?:,[a-zA-Z0-9_]+=[^,\]]*)*)\]"))
        {
            if (match.Index > position)
            {
                message.MessageContentList.Add(new TextContent
                {
                    Text = DecodeCqText(text[position..match.Index])
                });
            }

            var data = new Dictionary<string, string>();
            foreach (var parameter in match.Groups["data"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = parameter.IndexOf('=');
                // 参数中的逗号需单独解码，且必须先于 &amp; 解码，避免重复解码。
                data[parameter[..separator]] = DecodeCqText(parameter[(separator + 1)..].Replace("&#44;", ","));
            }

            AppendMessageContent(message, match.Groups["type"].Value, JsonSerializer.SerializeToElement(data));
            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            message.MessageContentList.Add(new TextContent
            {
                Text = DecodeCqText(text[position..])
            });
        }
    }

    private static string DecodeCqText(string text)
    {
        // 先解码方括号，最后解码 &amp;，避免转义后的实体文本被解码两次。
        return text.Replace("&#91;", "[").Replace("&#93;", "]").Replace("&amp;", "&");
    }

    private static long ReadMessageInteger(JsonElement value)
    {
        // 数组消息可能使用 JSON 数字，CQ 码参数则统一是字符串。
        return value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : long.Parse(value.GetString() ?? "", CultureInfo.InvariantCulture);
    }
    #endregion
}
