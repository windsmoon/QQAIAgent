using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using QQAIAgent.Abstractions.Tool;

namespace QQAIAgent.Tools;

public sealed class FetchWebPageTool : ITool, IDisposable
{
    #region fields
    private readonly HttpClient _httpClient;
    #endregion

    #region properties
    public string Name => "fetch_web_page";
    #endregion

    #region methods
    public FetchWebPageTool()
    {
        // 兼容使用 GBK、GB2312 等编码的网页，HTML 中的编码声明也由解析器读取。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false
        })
        {
            // 超时统一由工具执行器传入的 CancellationToken 控制。
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    }

    /// <summary>获取 HTTP/HTTPS 网页的静态正文，返回包含最终地址、标题和正文的 JSON。</summary>
    public async Task<string> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var url = arguments.RootElement.GetProperty("url").GetString();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) == false ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("url 必须是 HTTP 或 HTTPS 绝对地址。", nameof(argumentsJson));
        }

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentType = response.Content.Headers.ContentType;
        var mediaType = contentType?.MediaType;
        var isHtml = mediaType is null ||
            string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
        if (isHtml == false && string.Equals(mediaType, "text/plain", StringComparison.OrdinalIgnoreCase) == false)
        {
            throw new NotSupportedException($"不支持的网页内容类型：{mediaType}。仅支持 HTML 和纯文本。");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var charset = contentType?.CharSet;
        var document = new HtmlDocument();
        // 优先使用响应头编码；未声明时读取 HTML 的 meta 编码，最终由流读取器识别 BOM。
        var encoding = string.IsNullOrWhiteSpace(charset)
            ? (isHtml ? document.DetectEncoding(stream, true) : null) ?? Encoding.UTF8
            : Encoding.GetEncoding(charset.Trim('"'));
        stream.Position = 0;
        cancellationToken.ThrowIfCancellationRequested();

        var title = string.Empty;
        string content;
        if (isHtml)
        {
            document.Load(stream, encoding);
            var titleNode = document.DocumentNode.SelectSingleNode("//title");
            title = Regex.Replace(WebUtility.HtmlDecode(titleNode?.InnerText ?? string.Empty), @"\s+", " ").Trim();
            var body = document.DocumentNode.SelectSingleNode("//main") ??
                document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
            var text = new StringBuilder();
            AppendText(body, text, cancellationToken);
            content = Regex.Replace(text.ToString(), @"[^\S\r\n]+", " ");
            content = Regex.Replace(content, @" *[\r\n]+ *", "\n").Trim();
        }
        else
        {
            using var reader = new StreamReader(stream, encoding);
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Serialize(new
        {
            url = response.RequestMessage?.RequestUri?.AbsoluteUri ?? uri.AbsoluteUri,
            title,
            content
        });
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>按节点顺序提取文本，跳过非正文节点，并保留块级元素之间的换行。</summary>
    private static void AppendText(HtmlNode node, StringBuilder text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (node.NodeType == HtmlNodeType.Text)
        {
            text.Append(WebUtility.HtmlDecode(node.InnerText));
            return;
        }

        if (node.NodeType == HtmlNodeType.Comment ||
            node.Name is "head" or "script" or "style" or "noscript" or "template" ||
            node.Attributes.Contains("hidden") ||
            string.Equals(node.GetAttributeValue("aria-hidden", string.Empty), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isBlock = node.Name is "address" or "article" or "aside" or "blockquote" or "br" or "div" or
            "dl" or "dt" or "dd" or "fieldset" or "figcaption" or "figure" or "footer" or "form" or
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "header" or "hr" or "li" or "main" or
            "nav" or "ol" or "p" or "pre" or "section" or "table" or "tr" or "td" or "th" or "ul";
        if (isBlock)
        {
            text.Append('\n');
        }

        foreach (var child in node.ChildNodes)
        {
            AppendText(child, text, cancellationToken);
        }

        if (isBlock)
        {
            text.Append('\n');
        }
    }
    #endregion
}
