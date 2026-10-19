using SkiaSharp;

namespace QQAIAgent.Infrastructure.Image;

public static class ImageResizer
{
    /// <summary>最长边超过上限时等比缩小并重新编码；未超限或无法解码时原样返回。</summary>
    public static byte[] Shrink(byte[] image, int maxSide)
    {
        using var source = SKBitmap.Decode(image);
        if (source is null || Math.Max(source.Width, source.Height) <= maxSide)
        {
            return image;
        }

        var scale = (float)maxSide / Math.Max(source.Width, source.Height);
        using var resized = source.Resize(new SKImageInfo(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale))), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var encoded = SKImage.FromBitmap(resized).Encode(
            source.Info.AlphaType == SKAlphaType.Opaque ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, 90);
        return encoded.ToArray();
    }
}
