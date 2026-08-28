using System.Buffers.Binary;
using System.Text;
using Gelatin.Core.Imaging;
using Gelatin.Core.Models;

namespace Gelatin.Core.Format;

public static class GelFile
{
    public static ReadOnlySpan<byte> Magic => "GEL1"u8;
    public const int HeaderSize = 12;
    public const int MaxJsonBytes = 16 * 1024 * 1024;
    public const int MaxPngBytes = 512 * 1024 * 1024;

    public static GelDocument Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream);
    }

    public static GelDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[HeaderSize];
        ReadExactly(stream, header, "The GEL header is truncated.");
        if (!header[..4].SequenceEqual(Magic)) throw new GelFormatException("This is not a GEL1 file (wrong magic).");

        var jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        var pngLength = BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]);
        if (jsonLength is 0 or > MaxJsonBytes) throw new GelFormatException("The declared JSON payload length is invalid or too large.");
        if (pngLength is 0 or > MaxPngBytes) throw new GelFormatException("The declared PNG payload length is invalid or too large.");
        var expectedPayload = checked((long)jsonLength + pngLength);
        if (stream.CanSeek && stream.Length - stream.Position != expectedPayload)
        {
            if (stream.Length - stream.Position < expectedPayload) throw new GelFormatException("The GEL payload is truncated.");
            throw new GelFormatException("GEL1 files may not contain trailing bytes.");
        }

        var json = new byte[(int)jsonLength];
        ReadExactly(stream, json, "The JSON payload is truncated.");
        try { _ = new UTF8Encoding(false, true).GetString(json); }
        catch (DecoderFallbackException ex) { throw new GelFormatException("The GEL JSON payload is not valid UTF-8.", ex); }

        var png = new byte[(int)pngLength];
        ReadExactly(stream, png, "The PNG payload is truncated.");
        if (stream.ReadByte() != -1) throw new GelFormatException("GEL1 files may not contain trailing bytes.");

        var config = GelJson.Deserialize(json);
        GelValidator.Validate(config);
        var dimensions = ImageProcessor.GetDimensions(png);
        if (dimensions.Width != config.Image.Width || dimensions.Height != config.Image.Height)
            throw new GelFormatException($"Embedded PNG dimensions ({dimensions.Width}x{dimensions.Height}) do not match the GEL configuration ({config.Image.Width}x{config.Image.Height}).");
        return new GelDocument { Config = config, PngBytes = png };
    }

    public static byte[] WriteBytes(GelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        GelValidator.Validate(document.Config);
        var dimensions = ImageProcessor.GetDimensions(document.PngBytes);
        if (dimensions.Width != document.Config.Image.Width || dimensions.Height != document.Config.Image.Height)
            throw new GelFormatException("The processed PNG dimensions do not match the GEL configuration.");
        var json = GelJson.Serialize(document.Config);
        if (json.Length > MaxJsonBytes || document.PngBytes.Length > MaxPngBytes) throw new GelFormatException("The GEL asset exceeds the v1 size limits.");
        var result = new byte[checked(HeaderSize + json.Length + document.PngBytes.Length)];
        Magic.CopyTo(result.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)json.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8, 4), (uint)document.PngBytes.Length);
        json.CopyTo(result.AsSpan(HeaderSize));
        document.PngBytes.CopyTo(result.AsSpan(HeaderSize + json.Length));
        return result;
    }

    public static void WriteAtomic(string path, GelDocument document)
    {
        var bytes = WriteBytes(document);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new GelFormatException("The save destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, fullPath, true);
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
            throw;
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> target, string error)
    {
        var read = 0;
        while (read < target.Length)
        {
            var count = stream.Read(target[read..]);
            if (count == 0) throw new GelFormatException(error);
            read += count;
        }
    }
}
