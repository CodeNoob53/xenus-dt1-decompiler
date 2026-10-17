namespace XenusDt1Decompiler.Tests;

public class LutTests
{
    private const uint G16R16F = 112;
    private const uint A16B16G16R16F = 113;

    // Inverse of the atlas packing: R = lo(c0), G = hi(c0), B = lo(c1), A = hi(c1) (pixels are BGRA).
    private static ushort Unpack(byte[] bgra, int outWidth, int x, int y, int channel, int srcWidth, int srcHeight, int z)
    {
        int pair = channel / 2;
        int i = (((z * srcHeight) + y) * outWidth + pair * srcWidth + x) * 4;
        byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2], a = bgra[i + 3];
        return channel % 2 == 0 ? (ushort)(r | g << 8) : (ushort)(b | a << 8);
    }

    [Fact]
    public void G16R16F_Table_PacksLosslessly()
    {
        static ushort Code(int x, int y, int z, int c) => (ushort)(x + 256 * y + c * 7);
        var dds = Fixtures.LutDds(G16R16F, 2, 16, 8, 1, Code);

        Assert.True(DecompilerCore.IsRaw16Lut(dds));
        int w = 16, h = 8;
        Assert.True(DecompilerCore.TryDecodeRaw16Lut(dds, 2, ref w, ref h, out var px, out var reason), reason);
        Assert.Equal((16, 8), (w, h));
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 16; x++)
                for (int c = 0; c < 2; c++)
                    Assert.Equal(Code(x, y, 0, c), Unpack(px, w, x, y, c, 16, 8, 0));
    }

    [Fact]
    public void VolumeTable_KeepsAllChannelsAndSlices()
    {
        static ushort Code(int x, int y, int z, int c) => (ushort)((x << 11) | (y << 5) | z | (c << 14));
        var dds = Fixtures.LutDds(A16B16G16R16F, 4, 8, 4, 3, Code);

        int w = 8, h = 4;
        Assert.True(DecompilerCore.TryDecodeRaw16Lut(dds, 4, ref w, ref h, out var px, out var reason), reason);
        Assert.Equal((16, 12), (w, h)); // 2 channel pairs side by side, 3 slices stacked
        for (int z = 0; z < 3; z++)
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 8; x++)
                    for (int c = 0; c < 4; c++)
                        Assert.Equal(Code(x, y, z, c), Unpack(px, w, x, y, c, 8, 4, z));
    }

    [Fact]
    public void TruncatedTable_IsRejected()
    {
        var dds = Fixtures.LutDds(A16B16G16R16F, 4, 8, 4, 3, (_, _, _, _) => 1);
        var truncated = dds.AsSpan(0, dds.Length - 2).ToArray();
        int w = 8, h = 4;
        Assert.False(DecompilerCore.TryDecodeRaw16Lut(truncated, 4, ref w, ref h, out _, out var reason));
        Assert.Contains("truncated", reason);
    }

    [Fact]
    public void OrdinaryDds_IsNotALut()
    {
        Assert.False(DecompilerCore.IsRaw16Lut(Fixtures.Argb32Dds(4, 4, _ => (1, 2, 3, 4))));
    }
}
