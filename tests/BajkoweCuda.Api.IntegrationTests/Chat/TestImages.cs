using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Text;
using SkiaSharp;

namespace BajkoweCuda.Api.IntegrationTests.Chat;

/// <summary>Images for the upload tests, made in memory.</summary>
internal static class TestImages
{
    /// <summary>A secret the re-encoding must remove (it stands for GPS data and hidden payloads).</summary>
    public const string Secret = "SECRET-GPS-52.2297N-21.0122E";

    public static byte[] Jpeg(int width = 300, int height = 200) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    public static byte[] Png(int width = 300, int height = 200) => Encode(width, height, SKEncodedImageFormat.Png);

    public static byte[] Webp(int width = 300, int height = 200) => Encode(width, height, SKEncodedImageFormat.Webp);

    /// <summary>
    /// A JPEG whose EXIF says "rotate 90° clockwise" (orientation 6, as phones write it), with the secret in a comment
    /// segment and in an EXIF field.
    /// </summary>
    public static byte[] JpegWithExif(int width = 300, int height = 200)
    {
        var jpeg = Jpeg(width, height);
        var secret = Encoding.ASCII.GetBytes(Secret);

        // EXIF (big endian TIFF): IFD0 with Orientation = 6 and ImageDescription = the secret.
        using var tiff = new MemoryStream();
        tiff.Write("MM\0*"u8);
        WriteUInt32(tiff, 8);
        WriteUInt16(tiff, 2);
        WriteEntry(tiff, tag: 0x0112, type: 3, count: 1, value: 6u << 16);
        WriteEntry(tiff, tag: 0x010E, type: 2, count: (uint)secret.Length, value: 8 + 2 + (2 * 12) + 4);
        WriteUInt32(tiff, 0);
        tiff.Write(secret);
        var exif = Concat("Exif\0\0"u8.ToArray(), tiff.ToArray());

        var app1 = Segment(0xE1, exif);
        var comment = Segment(0xFE, secret);
        return Concat(jpeg[..2], app1, comment, jpeg[2..]);
    }

    /// <summary>A PNG whose header announces 8000 × 6000 = 48 megapixels (the pixel data itself is not needed).</summary>
    public static byte[] HugePngHeader()
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, 8000);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 6000);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB
        // An empty zlib stream as image data: enough for the decoder to read the header.
        byte[] emptyZlib = [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];
        return Concat([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], Chunk("IHDR", header), Chunk("IDAT", emptyZlib), Chunk("IEND", []));
    }

    public static MultipartFormDataContent Form(byte[] file, string fileName = "photo.jpg", string contentType = "image/jpeg")
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    public static (int Width, int Height, SKEncodedImageFormat Format) Describe(byte[] file)
    {
        using var codec = SKCodec.Create(new MemoryStream(file));
        return (codec.Info.Width, codec.Info.Height, codec.EncodedFormat);
    }

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Pink);
            using var paint = new SKPaint { Color = SKColors.Purple };
            canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        return segment;
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        typeBytes.CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32(Concat(typeBytes, data)));
        return chunk;
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static void WriteEntry(Stream stream, ushort tag, ushort type, uint count, uint value)
    {
        WriteUInt16(stream, tag);
        WriteUInt16(stream, type);
        WriteUInt32(stream, count);
        WriteUInt32(stream, value);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
