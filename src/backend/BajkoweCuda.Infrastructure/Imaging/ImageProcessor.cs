using SkiaSharp;

namespace BajkoweCuda.Infrastructure.Imaging;

/// <summary>A re-encoded image: the display copy (at most 1600 px) and the thumbnail (at most 480 px), both WebP.</summary>
public sealed record ProcessedImage(byte[] Display, int Width, int Height, byte[] Thumbnail)
{
    public const string ContentType = "image/webp";
}

/// <summary>Why an upload was refused. The values are the API's error codes.</summary>
public static class ImageErrors
{
    public const string TooLarge = "files.too_large";
    public const string UnsupportedType = "files.unsupported_type";
    public const string TooManyPixels = "files.too_many_pixels";
    public const string Unreadable = "files.unreadable";
}

/// <summary>
/// The upload pipeline of docs/PLAN.md §8.4. The type is decided by the file's first bytes (JPEG, PNG or WebP), never by
/// its name or the browser's content type. Every image is decoded and encoded again: what is stored contains only
/// pixels, so EXIF data (GPS position, camera serial numbers) and anything hidden in the file are gone. The EXIF
/// orientation is applied first, so photos from phones stay upright.
/// </summary>
public static class ImageProcessor
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    public const int DisplaySize = 1600;
    public const int ThumbnailSize = 480;

    private const int DisplayQuality = 82;
    private const int ThumbnailQuality = 75;

    /// <summary>Returns the image, or one of <see cref="ImageErrors"/>.</summary>
    public static (ProcessedImage? Image, string? Error) Process(ReadOnlySpan<byte> file)
    {
        if (file.Length > MaxBytes)
        {
            return (null, ImageErrors.TooLarge);
        }

        if (Detect(file) is not { } format)
        {
            return (null, ImageErrors.UnsupportedType);
        }

        using var data = SKData.CreateCopy(file);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != format)
        {
            return (null, ImageErrors.Unreadable);
        }

        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            return (null, ImageErrors.TooManyPixels);
        }

        using var decoded = Decode(codec, DisplaySize);
        if (decoded is null)
        {
            return (null, ImageErrors.Unreadable);
        }

        using var upright = Orient(decoded, codec.EncodedOrigin);
        var (display, width, height) = Encode(upright, DisplaySize, DisplayQuality);
        var (thumbnail, _, _) = Encode(upright, ThumbnailSize, ThumbnailQuality);
        return (new ProcessedImage(display, width, height, thumbnail), null);
    }

    /// <summary>The format announced by the file's signature, or <c>null</c> for anything else.</summary>
    public static SKEncodedImageFormat? Detect(ReadOnlySpan<byte> file)
    {
        if (file.Length >= 3 && file[0] == 0xFF && file[1] == 0xD8 && file[2] == 0xFF)
        {
            return SKEncodedImageFormat.Jpeg;
        }

        if (file.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return SKEncodedImageFormat.Png;
        }

        if (file.Length >= 12 && file[..4].SequenceEqual("RIFF"u8) && file[8..12].SequenceEqual("WEBP"u8))
        {
            return SKEncodedImageFormat.Webp;
        }

        return null;
    }

    /// <summary>
    /// Decodes into sRGB. JPEG decoders can scale down while decoding (1/2, 1/4, 1/8), which keeps a 40-megapixel
    /// photo from needing 160 MB of memory; other formats decode at full size.
    /// </summary>
    private static SKBitmap? Decode(SKCodec codec, int maxSide)
    {
        var scale = Math.Min(1f, (float)maxSide / Math.Max(codec.Info.Width, codec.Info.Height));
        var size = codec.GetScaledDimensions(scale);
        // The decoder rounds to a scale it supports (eighths for JPEG), possibly below the wanted size: step up until
        // the decoded image is at least that big; the exact size comes from the resize afterwards.
        while (Math.Max(size.Width, size.Height) < Math.Min(maxSide, Math.Max(codec.Info.Width, codec.Info.Height)) && scale < 1f)
        {
            scale = Math.Min(1f, scale + 0.125f);
            size = codec.GetScaledDimensions(scale);
        }
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());

        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            // A truncated or damaged file is refused rather than stored half grey.
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    /// <summary>Draws the pixels the way the EXIF orientation says they are meant to be seen.</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var w = source.Width;
        var h = source.Height;
        var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        // x' = ScaleX·x + SkewX·y + TransX;  y' = SkewY·x + ScaleY·y + TransY
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        var result = new SKBitmap(new SKImageInfo(swapped ? h : w, swapped ? w : h, source.ColorType, source.AlphaType, source.ColorSpace));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }

    private static (byte[] Bytes, int Width, int Height) Encode(SKBitmap source, int maxSide, int quality)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = scale < 1.0
            ? source.Resize(new SKImageInfo(width, height, source.ColorType, source.AlphaType, source.ColorSpace), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : null;
        using var image = SKImage.FromBitmap(resized ?? source);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, quality);
        return (encoded.ToArray(), width, height);
    }
}
