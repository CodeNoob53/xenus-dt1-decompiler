namespace XenusDt1Decompiler.Tests;

public class NormalMapTests
{
    // Cache layout of a normal map: B slot = Y, G slot = Z, R slot = 0, A slot = X.
    private static byte[] SwizzledNormalMap(int w = 8, int h = 8) =>
        Fixtures.Argb32Dds(w, h, i => (b: (byte)(100 + i % 50), g: (byte)(200 + i % 50), r: 0, a: (byte)(i % 256)));

    [Fact]
    public void SwizzledNormalMap_IsDetected()
    {
        Assert.True(DecompilerCore.IsSwizzledNormalMap(SwizzledNormalMap()));
    }

    [Fact]
    public void Remap_GivesStandardRgba()
    {
        var dds = SwizzledNormalMap();
        var fixedDds = DecompilerCore.FixNormalMapChannels(dds);

        Assert.Equal(0x000000FFu, BitConverter.ToUInt32(fixedDds, 92));
        Assert.Equal(0x0000FF00u, BitConverter.ToUInt32(fixedDds, 96));
        Assert.Equal(0x00FF0000u, BitConverter.ToUInt32(fixedDds, 100));
        Assert.Equal(0xFF000000u, BitConverter.ToUInt32(fixedDds, 104));
        for (int i = 128; i < dds.Length; i += 4)
        {
            Assert.Equal(dds[i + 3], fixedDds[i]);     // R = X
            Assert.Equal(dds[i], fixedDds[i + 1]);     // G = Y
            Assert.Equal(dds[i + 1], fixedDds[i + 2]); // B = Z
            Assert.Equal(255, fixedDds[i + 3]);        // A = opaque
        }
    }

    [Fact]
    public void TextureWithRedChannel_IsNotANormalMap()
    {
        var diffuse = Fixtures.Argb32Dds(8, 8, i => (b: 120, g: 200, r: (byte)(i == 37 ? 1 : 0), a: 255));
        Assert.False(DecompilerCore.IsSwizzledNormalMap(diffuse));
    }

    [Fact]
    public void BlackAlphaMask_IsNotANormalMap()
    {
        // R slot is 0, but so is Z: e.g. Boiling Point scope and explosion masks.
        var mask = Fixtures.Argb32Dds(8, 8, i => (b: 0, g: 0, r: 0, a: (byte)(i * 4)));
        Assert.False(DecompilerCore.IsSwizzledNormalMap(mask));
    }

    [Fact]
    public void CompressedDds_IsNotANormalMap()
    {
        var dxt1 = Fixtures.DdsHeader(4, 4, 0x4, 0x31545844 /* DXT1 */, 0, 0, 0, 0, 0).Concat(new byte[8]).ToArray();
        Assert.False(DecompilerCore.IsSwizzledNormalMap(dxt1));
    }

    [Fact]
    public void DecodeOneFile_FixesNormalMapAndStripsSuffix()
    {
        var dir = Directory.CreateTempSubdirectory("dt1test");
        try
        {
            var input = Path.Combine(dir.FullName, "ROCK_N_TGA.DT1");
            File.WriteAllBytes(input, Fixtures.DtZlib(SwizzledNormalMap()));
            var output = Path.Combine(dir.FullName, "out");

            var errors = new List<string>();
            bool ok = DecompilerCore.DecodeOneFile(input, output, null, "dds", null, _ => { }, errors.Add);

            Assert.True(ok, string.Join("\n", errors));
            var result = File.ReadAllBytes(Path.Combine(output, "ROCK_N.dds"));
            Assert.Equal(DecompilerCore.FixNormalMapChannels(SwizzledNormalMap()), result);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
