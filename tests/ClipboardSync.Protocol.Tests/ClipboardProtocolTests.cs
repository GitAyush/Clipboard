using ClipboardSync.Protocol;

namespace ClipboardSync.Protocol.Tests;

public sealed class ClipboardProtocolTests
{
    [Fact]
    public void ComputeTextHashUtf8_IsDeterministic()
    {
        var h1 = ClipboardProtocol.ComputeTextHashUtf8("hello");
        var h2 = ClipboardProtocol.ComputeTextHashUtf8("hello");

        Assert.Equal(32, h1.Length);
        Assert.True(ClipboardProtocol.HashEquals(h1, h2));
    }

    [Fact]
    public void ComputeTextHashUtf8_DiffersForDifferentText()
    {
        var h1 = ClipboardProtocol.ComputeTextHashUtf8("hello");
        var h2 = ClipboardProtocol.ComputeTextHashUtf8("hello!");

        Assert.False(ClipboardProtocol.HashEquals(h1, h2));
    }

    [Fact]
    public void ComputeTextHashUtf8_Throws_WhenOverMaxBytes()
    {
        // 1 char = 1 byte for ASCII
        var tooLarge = new string('a', ClipboardProtocol.MaxTextBytesUtf8 + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => ClipboardProtocol.ComputeTextHashUtf8(tooLarge));
    }

    [Fact]
    public void HashToHex_Returns64Chars()
    {
        var hash = ClipboardProtocol.ComputeTextHashUtf8("hello");
        var hex = ClipboardProtocol.HashToHex(hash);

        Assert.Equal(64, hex.Length);
    }
}


