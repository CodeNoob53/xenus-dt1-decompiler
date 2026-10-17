namespace XenusDt1Decompiler.Tests;

public class DecodeDirectoryTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("dt1dir");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Input(string relativePath, byte[] dds)
    {
        var path = Path.Combine(_dir.FullName, "in", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Fixtures.DtZlib(dds));
        return path;
    }

    private static byte[] Dds(byte shade) => Fixtures.Argb32Dds(4, 4, _ => (shade, shade, shade, 255));

    [Fact]
    public void SameBaseNameWithDifferentSuffix_ClashesOnlyWhenFormatIsForced()
    {
        var root = Path.Combine(_dir.FullName, "in");
        var bmp = Input(@"SKY\SKY00_BMP.DT1", Dds(1));
        var tga = Input(@"SKY\SKY00_TGA.DT1", Dds(2));
        var other = Input(@"SKY\SKY01_TGA.DT1", Dds(3));
        var files = new[] { bmp, tga, other };

        Assert.Empty(DecompilerCore.FindOutputNameClashes(files, root, null)); // Auto: SKY00.bmp + SKY00.tga
        Assert.Equal(new[] { bmp, tga }.ToHashSet(), DecompilerCore.FindOutputNameClashes(files, root, "dds"));
    }

    [Fact]
    public void DecodeDirectory_KeepsBothClashingTextures()
    {
        Input(@"SKY\SKY00_BMP.DT1", Dds(1));
        Input(@"SKY\SKY00_TGA.DT1", Dds(2));
        Input(@"SKY\SKY01_TGA.DT1", Dds(3));
        var output = Path.Combine(_dir.FullName, "out");

        var errors = new List<string>();
        var (ok, fail) = DecompilerCore.DecodeDirectory(Path.Combine(_dir.FullName, "in"), output, "dds", _ => { }, errors.Add);

        Assert.Equal((3, 0), (ok, fail));
        Assert.Equal(Dds(1), File.ReadAllBytes(Path.Combine(output, "SKY", "SKY00_BMP.dds")));
        Assert.Equal(Dds(2), File.ReadAllBytes(Path.Combine(output, "SKY", "SKY00_TGA.dds")));
        Assert.Equal(Dds(3), File.ReadAllBytes(Path.Combine(output, "SKY", "SKY01.dds")));
        Assert.False(File.Exists(Path.Combine(output, "SKY", "SKY00.dds")));
    }

    [Fact]
    public void DecodeDirectory_ProcessesManyFilesInParallel()
    {
        for (int i = 0; i < 200; i++)
            Input($@"D{i % 7}\TEX{i:D3}_TGA.DT1", Dds((byte)i));
        Input(@"BROKEN_TGA.DT1", Dds(0));
        File.WriteAllBytes(Path.Combine(_dir.FullName, "in", "BROKEN_TGA.DT1"), new byte[] { 1, 2, 3 });
        var output = Path.Combine(_dir.FullName, "out");

        int lastProgress = 0;
        var progressLock = new object();
        var (ok, fail) = DecompilerCore.DecodeDirectory(Path.Combine(_dir.FullName, "in"), output, "dds",
            _ => { }, _ => { }, (done, total) => { lock (progressLock) lastProgress = Math.Max(lastProgress, done); });

        Assert.Equal((200, 1), (ok, fail));
        Assert.Equal(201, lastProgress);
        for (int i = 0; i < 200; i++)
            Assert.Equal(Dds((byte)i), File.ReadAllBytes(Path.Combine(output, $"D{i % 7}", $"TEX{i:D3}.dds")));
    }

    [Fact]
    public void DecodeDirectory_StopsWhenCancelled()
    {
        for (int i = 0; i < 50; i++)
            Input($"TEX{i:D2}_TGA.DT1", Dds((byte)i));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var warnings = new List<string>();
        var (ok, fail) = DecompilerCore.DecodeDirectory(Path.Combine(_dir.FullName, "in"), Path.Combine(_dir.FullName, "out"),
            "dds", _ => { }, warnings.Add, cancellationToken: cts.Token);

        Assert.True(ok + fail < 50);
        Assert.Contains(warnings, w => w.Contains("Cancelled"));
    }
}
