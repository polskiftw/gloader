using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Gelatin.Core.Models;
using SkiaSharp;

namespace Gelatin.Core.Imaging;

public sealed class RgbaBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public RgbaBuffer(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }
}

public static class RawRgbaCodec
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static bool IsPng(ReadOnlySpan<byte> encoded)
        => encoded.Length >= PngSignature.Length && encoded[..PngSignature.Length].SequenceEqual(PngSignature);

    public static RgbaBuffer Decode(ReadOnlySpan<byte> encoded)
    {
        try
        {
            using var data = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(data) ?? throw new GelFormatException("The image is unsupported or corrupt.");
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success) throw new GelFormatException($"The image decoder failed ({result}).");
            var pixels = new byte[checked(info.Width * info.Height * 4)];
            for (var y = 0; y < info.Height; y++)
                Marshal.Copy(IntPtr.Add(bitmap.GetPixels(), checked(y * bitmap.RowBytes)), pixels, checked(y * info.Width * 4), checked(info.Width * 4));
            return new RgbaBuffer(info.Width, info.Height, pixels);
        }
        catch (GelFormatException) { throw; }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw new GelFormatException("The image is unsupported or corrupt.", ex);
        }
    }

    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (width < 1 || height < 1 || pixels.Length != checked(width * height * 4))
            throw new GelFormatException("The processed RGBA image buffer is invalid.");
        try
        {
            using var output = new MemoryStream();
            output.Write(PngSignature);
            Span<byte> ihdr = stackalloc byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
            BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..8], (uint)height);
            ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
            WriteChunk(output, "IHDR"u8, ihdr);

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                var row = new byte[checked(width * 4 + 1)];
                for (var y = 0; y < height; y++)
                {
                    row[0] = 0;
                    pixels.Slice(checked(y * width * 4), checked(width * 4)).CopyTo(row.AsSpan(1));
                    zlib.Write(row);
                }
            }
            WriteChunk(output, "IDAT"u8, compressed.ToArray());
            WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
            return output.ToArray();
        }
        catch (GelFormatException) { throw; }
        catch (Exception ex) when (ex is IOException or OverflowException or ArgumentException)
        {
            throw new GelFormatException("The processed image could not be encoded as PNG.", ex);
        }
    }

    public static byte[] Encode(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.Width < 1 || bitmap.Height < 1) throw new GelFormatException("The processed image has invalid dimensions.");
        var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        for (var y = 0; y < bitmap.Height; y++)
            Marshal.Copy(IntPtr.Add(bitmap.GetPixels(), checked(y * bitmap.RowBytes)), pixels, checked(y * bitmap.Width * 4), checked(bitmap.Width * 4));
        return Encode(bitmap.Width, bitmap.Height, pixels);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length); output.Write(type); output.Write(data);
        var crc = 0xffffffffu;
        foreach (var value in type) crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        foreach (var value in data) crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        crc ^= 0xffffffffu;
        Span<byte> encodedCrc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(encodedCrc, crc);
        output.Write(encodedCrc);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
