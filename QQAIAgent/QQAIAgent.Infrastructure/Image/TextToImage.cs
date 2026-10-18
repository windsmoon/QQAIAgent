using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace QQAIAgent.Infrastructure.Image;

public static class TextToImage
{
    #region methods
    /// <summary>将纯文本绘制为一张白底黑字 PNG，保留换行并按宽度自动折行。</summary>
    public static byte[] Render(string text, ILogger logger)
    {
        var startedAt = Stopwatch.GetTimestamp();
        const int width = 960;
        const int padding = 32;
        const float fontSize = 26;

        // TODO GC
        using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei");
        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Split('\n'))
        {
            var line = "";
            // 按完整文本元素折行，避免拆开代理对或组合字符。
            var elements = StringInfo.GetTextElementEnumerator(paragraph);
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                if (line.Length > 0 && font.MeasureText(line + element) > width - padding * 2)
                {
                    // 如果加入新元素会超宽，就先把不包含新元素的当前行保存，再清空 line，准备下一行。
                    lines.Add(line);
                    line = string.Empty;
                }

                line += element;
            }

            lines.Add(line);
        }

        var metrics = font.Metrics;
        var fontHeight = metrics.Descent - metrics.Ascent;
        var lineHeight = MathF.Ceiling(fontHeight + 8);
        var height = (int)MathF.Ceiling(padding * 2 + fontHeight + (lines.Count - 1) * lineHeight);
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        for (var index = 0; index < lines.Count; index++)
        {
            // DrawText 的纵坐标是文字基线，减去负数 Ascent 后才是首行基线的位置。
            canvas.DrawText(lines[index], padding, padding - metrics.Ascent + index * lineHeight,
                SKTextAlign.Left, font, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        var result = data.ToArray();
        logger.LogInformation("文字转图片渲染耗时：耗时={ElapsedMilliseconds:F1} ms",
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        return result;
    }
    #endregion
}
