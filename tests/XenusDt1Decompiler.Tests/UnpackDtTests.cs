namespace XenusDt1Decompiler.Tests;

public class UnpackDtTests
{
    [Fact]
    public void Method0_ReturnsStoredData()
    {
        var data = Fixtures.Random(1000, 1);
        Assert.Equal(data, DecompilerCore.UnpackDt(Fixtures.DtStored(data)));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)] // payload < 0x100 bytes: not encrypted
    public void Methods4To7_DecodeXoredZlib(int method)
    {
        var data = method == 7 ? new byte[300] : Fixtures.Repetitive(50_000, method);
        Assert.Equal(data, DecompilerCore.UnpackDt(Fixtures.DtZlib(data, method)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Methods1To3_DecodeNrv2e(int method)
    {
        var data = Fixtures.Repetitive(20_000, method);
        Assert.Equal(data, DecompilerCore.UnpackDt(Fixtures.DtNrv2e(data, method)));
    }

    [Fact]
    public void Method1_IsNotXored()
    {
        // Method 1 stream decoded as method 2 (i.e. with the XOR applied) must not give the same data.
        var data = Fixtures.Repetitive(5_000, 11);
        var dt = Fixtures.DtNrv2e(data, method: 1);
        dt[3] = (byte)((dt[3] & 0x0F) | 0x20); // relabel as method 2
        var ex = Record.Exception(() => DecompilerCore.UnpackDt(dt));
        if (ex is null)
            Assert.NotEqual(data, DecompilerCore.UnpackDt(dt));
    }

    [Fact]
    public void Method7Encrypted_IsReportedAsUnsupported()
    {
        var data = Fixtures.Random(4000, 2); // compresses to > 0x100 bytes
        var dt = Fixtures.DtZlib(data, method: 7);
        var ex = Assert.Throws<NotSupportedException>(() => DecompilerCore.UnpackDt(dt));
        Assert.Contains("method 7", ex.Message);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(15)]
    public void UnknownMethod_Throws(int method)
    {
        var dt = Fixtures.Dt(method, new byte[10], new byte[10]);
        Assert.Throws<InvalidDataException>(() => DecompilerCore.UnpackDt(dt));
    }

    [Fact]
    public void TooSmallFile_Throws()
    {
        Assert.Throws<InvalidDataException>(() => DecompilerCore.UnpackDt(new byte[5]));
    }

    [Fact]
    public void PackedSizeBeyondFile_Throws()
    {
        var dt = Fixtures.DtZlib(Fixtures.Random(500, 3));
        var truncated = dt.AsSpan(0, dt.Length - 10).ToArray();
        Assert.Throws<InvalidDataException>(() => DecompilerCore.UnpackDt(truncated));
    }

    [Fact]
    public void ZlibShorterThanHeaderSize_Throws()
    {
        var data = Fixtures.Random(500, 4);
        var dt = Fixtures.DtZlib(data);
        BitConverter.GetBytes((5u << 28) | 600u).CopyTo(dt, 0); // header claims 600 bytes
        Assert.Throws<InvalidDataException>(() => DecompilerCore.UnpackDt(dt));
    }

    [Fact]
    public void Nrv2eSizeMismatch_Throws()
    {
        var data = Fixtures.Repetitive(3000, 5);
        var dt = Fixtures.DtNrv2e(data);
        BitConverter.GetBytes((2u << 28) | 2999u).CopyTo(dt, 0); // one byte too small
        Assert.Throws<InvalidDataException>(() => DecompilerCore.UnpackDt(dt));
    }
}
