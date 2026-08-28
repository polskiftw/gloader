using System.Buffers.Binary;
using System.Text;
using Gelatin.Core.Format;
using Gelatin.Core.Models;

namespace Gelatin.Tests;

public sealed class GelFileTests
{
    [Fact]
    public void ValidAssetRoundTripsEveryConfigurationField()
    {
        var original = TestAssets.Document();
        var bytes = GelFile.WriteBytes(original);
        var reopened = GelFile.Read(new MemoryStream(bytes));
        Assert.Equal(original.Config.Image.Width, reopened.Config.Image.Width);
        Assert.Equal(original.Config.Image.Height, reopened.Config.Image.Height);
        Assert.Equal(Convert.ToHexString(GelJson.Serialize(original.Config)), Convert.ToHexString(GelJson.Serialize(reopened.Config)));
        Assert.Equal(original.PngBytes, reopened.PngBytes);
    }

    [Fact]
    public void HeaderMagicLengthsJsonAndPngSignatureAreExact()
    {
        var bytes = GelFile.WriteBytes(TestAssets.Document());
        Assert.Equal("GEL1", Encoding.ASCII.GetString(bytes, 0, 4));
        var jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
        var pngLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        Assert.Equal(bytes.Length, 12L + jsonLength + pngLength);
        var json = bytes.AsSpan(12, checked((int)jsonLength));
        Assert.DoesNotContain('\uFFFD', new UTF8Encoding(false, true).GetString(json));
        Assert.Equal(1, GelJson.Deserialize(json).SchemaVersion);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.AsSpan(12 + (int)jsonLength, 8).ToArray());
    }

    [Fact]
    public void WrongMagicIsRejected()
    {
        var bytes = GelFile.WriteBytes(TestAssets.Document());
        bytes[0] = (byte)'X';
        AssertDomainError(bytes, "wrong magic");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(11)]
    public void TruncatedHeaderIsRejected(int length)
        => Assert.Throws<GelFormatException>(() => GelFile.Read(new MemoryStream(new byte[length])));

    [Fact]
    public void TruncatedJsonIsRejected()
    {
        var bytes = GelFile.WriteBytes(TestAssets.Document());
        Assert.Throws<GelFormatException>(() => GelFile.Read(new MemoryStream(bytes[..20])));
    }

    [Fact]
    public void TruncatedPngIsRejected()
    {
        var bytes = GelFile.WriteBytes(TestAssets.Document());
        Assert.Throws<GelFormatException>(() => GelFile.Read(new MemoryStream(bytes[..^1])));
    }

    [Fact]
    public void TrailingBytesAreRejected()
    {
        var bytes = GelFile.WriteBytes(TestAssets.Document()).Concat(new byte[] { 99 }).ToArray();
        AssertDomainError(bytes, "trailing");
    }

    [Theory]
    [InlineData(0xFFFFFFFFu, 1u)]
    [InlineData(1u, 0xFFFFFFFFu)]
    [InlineData(0u, 100u)]
    public void DangerousDeclaredLengthsAreRejectedBeforeAllocation(uint jsonLength, uint pngLength)
    {
        var bytes = new byte[12];
        "GEL1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), pngLength);
        Assert.Throws<GelFormatException>(() => GelFile.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void InvalidJsonHasFriendlyDomainError()
    {
        var bytes = Container("{"u8.ToArray(), TestAssets.Png());
        var error = Assert.Throws<GelFormatException>(() => GelFile.Read(new MemoryStream(bytes)));
        Assert.Contains("valid JSON", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidUtf8IsRejected()
    {
        var bytes = Container([0xC3, 0x28], TestAssets.Png());
        AssertDomainError(bytes, "UTF-8");
    }

    [Fact]
    public void PngDimensionMismatchIsRejected()
    {
        var document = TestAssets.Document();
        document.Config.Image.Width++;
        var json = GelJson.Serialize(document.Config);
        var bytes = Container(json, document.PngBytes);
        AssertDomainError(bytes, "dimensions");
    }

    [Fact]
    public void SchemaVersionOtherThanOneIsRejected()
    {
        var document = TestAssets.Document();
        document.Config.SchemaVersion = 2;
        var bytes = Container(GelJson.Serialize(document.Config), document.PngBytes);
        AssertDomainError(bytes, "schemaVersion");
    }

    [Fact]
    public void MissingRequiredJsonPropertyIsRejected()
    {
        var document = TestAssets.Document();
        var json = Encoding.UTF8.GetString(GelJson.Serialize(document.Config));
        json = json.Replace("\"assetName\":\"Round Trip Gel\",", string.Empty, StringComparison.Ordinal);
        var bytes = Container(Encoding.UTF8.GetBytes(json), document.PngBytes);
        AssertDomainError(bytes, "required");
    }

    [Fact]
    public void AtomicSaveKeepsExistingDestinationWhenSerializationFails()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gelatin-{Guid.NewGuid():N}.gel");
        try
        {
            var valid = TestAssets.Document();
            GelFile.WriteAtomic(path, valid);
            var before = File.ReadAllBytes(path);
            var invalid = TestAssets.Document();
            invalid.Config.Image.Width = 0;
            Assert.Throws<GelFormatException>(() => GelFile.WriteAtomic(path, invalid));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void AssertDomainError(byte[] bytes, string text)
    {
        var error = Assert.Throws<GelFormatException>(() => GelFile.Read(new MemoryStream(bytes)));
        Assert.Contains(text, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Container(byte[] json, byte[] png)
    {
        var bytes = new byte[12 + json.Length + png.Length];
        "GEL1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)json.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)png.Length);
        json.CopyTo(bytes, 12);
        png.CopyTo(bytes, 12 + json.Length);
        return bytes;
    }
}
