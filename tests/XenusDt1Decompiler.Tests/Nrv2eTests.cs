namespace XenusDt1Decompiler.Tests;

public class Nrv2eTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(4096)]
    public void RandomData_RoundTrips(int length)
    {
        var data = Fixtures.Random(length, length);
        Assert.Equal(data, DecompilerCore.DecompressNrv2e(Nrv2eEncoder.Encode(data), data.Length));
    }

    [Fact]
    public void RepetitiveData_UsesMatchesAndRoundTrips()
    {
        var data = Fixtures.Repetitive(30_000, 42);
        var stream = Nrv2eEncoder.Encode(data);
        Assert.True(stream.Length < data.Length / 2, "expected the encoder to emit matches");
        Assert.Equal(data, DecompilerCore.DecompressNrv2e(stream, data.Length));
    }

    [Fact]
    public void LongRuns_UseGammaLengths()
    {
        var data = new byte[10_000];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i / 700); // runs of 700 equal bytes
        Assert.Equal(data, DecompilerCore.DecompressNrv2e(Nrv2eEncoder.Encode(data), data.Length));
    }

    [Fact]
    public void TruncatedStream_Throws()
    {
        var data = Fixtures.Repetitive(5000, 7);
        var stream = Nrv2eEncoder.Encode(data);
        var truncated = stream.AsSpan(0, stream.Length / 2).ToArray();
        Assert.Throws<InvalidDataException>(() => DecompilerCore.DecompressNrv2e(truncated, data.Length));
    }

    [Fact]
    public void OutputLargerThanExpected_Throws()
    {
        var data = Fixtures.Repetitive(5000, 8);
        Assert.Throws<InvalidDataException>(() => DecompilerCore.DecompressNrv2e(Nrv2eEncoder.Encode(data), data.Length - 1));
    }

    [Fact]
    public void TrailingBytes_Throw()
    {
        var data = Fixtures.Random(200, 9);
        var stream = Nrv2eEncoder.Encode(data).Concat(new byte[] { 0, 0 }).ToArray();
        Assert.Throws<InvalidDataException>(() => DecompilerCore.DecompressNrv2e(stream, data.Length));
    }
}
