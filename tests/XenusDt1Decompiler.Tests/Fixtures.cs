using System.IO.Compression;

namespace XenusDt1Decompiler.Tests;

// Builds synthetic DT1 containers and DDS images, so the tests need no game files.
internal static class Fixtures
{
    public static byte[] Dt(int method, byte[] unpacked, byte[] payload, int keyIndex = 0)
    {
        var dt = new byte[8 + payload.Length];
        BitConverter.GetBytes(((uint)method << 28) | (uint)unpacked.Length).CopyTo(dt, 0);
        BitConverter.GetBytes(((uint)keyIndex << 27) | (uint)dt.Length).CopyTo(dt, 4);
        payload.CopyTo(dt, 8);
        return dt;
    }

    public static byte[] DtStored(byte[] data) => Dt(0, data, data);

    public static byte[] DtZlib(byte[] data, int method = 5)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data);
        return Dt(method, data, Obfuscate(ms.ToArray()));
    }

    public static byte[] DtNrv2e(byte[] data, int method = 2)
    {
        var stream = Nrv2eEncoder.Encode(data);
        return Dt(method, data, method == 1 ? stream : Obfuscate(stream));
    }

    // The first 16 payload bytes are XOR-ed with 0x6E (methods 2..7).
    public static byte[] Obfuscate(byte[] payload)
    {
        var p = (byte[])payload.Clone();
        for (int i = 0; i < Math.Min(16, p.Length); i++)
            p[i] ^= 0x6E;
        return p;
    }

    public static byte[] Random(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    // Data with many repeats at short and long distances, so the encoder emits matches.
    public static byte[] Repetitive(int length, int seed)
    {
        var rnd = new Random(seed);
        var data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            int choice = rnd.Next(10);
            if (i > 0x600 && choice == 0)
                data[i] = data[i - 0x5A0]; // far match (> 0x500)
            else if (i > 64 && choice < 6)
                data[i] = data[i - 1 - rnd.Next(3)];
            else
                data[i] = (byte)rnd.Next(8);
        }
        return data;
    }

    public static byte[] DdsHeader(int width, int height, uint pfFlags, uint fourCc, int bpp,
        uint rMask, uint gMask, uint bMask, uint aMask, int depth = 1)
    {
        var h = new byte[128];
        "DDS "u8.CopyTo(h);
        BitConverter.GetBytes(124).CopyTo(h, 4);
        uint flags = 0x1007 | (depth > 1 ? 0x800000u : 0);
        BitConverter.GetBytes(flags).CopyTo(h, 8);
        BitConverter.GetBytes(height).CopyTo(h, 12);
        BitConverter.GetBytes(width).CopyTo(h, 16);
        BitConverter.GetBytes(depth > 1 ? depth : 0).CopyTo(h, 24);
        BitConverter.GetBytes(32).CopyTo(h, 76);
        BitConverter.GetBytes(pfFlags).CopyTo(h, 80);
        BitConverter.GetBytes(fourCc).CopyTo(h, 84);
        BitConverter.GetBytes(bpp).CopyTo(h, 88);
        BitConverter.GetBytes(rMask).CopyTo(h, 92);
        BitConverter.GetBytes(gMask).CopyTo(h, 96);
        BitConverter.GetBytes(bMask).CopyTo(h, 100);
        BitConverter.GetBytes(aMask).CopyTo(h, 104);
        if (depth > 1)
            BitConverter.GetBytes(0x200000).CopyTo(h, 112); // DDSCAPS2_VOLUME
        return h;
    }

    // A8R8G8B8 DDS as the cache stores it (byte order B, G, R, A).
    public static byte[] Argb32Dds(int width, int height, Func<int, (byte b, byte g, byte r, byte a)> pixel)
    {
        var header = DdsHeader(width, height, 0x41, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000);
        var dds = new byte[128 + width * height * 4];
        header.CopyTo(dds, 0);
        for (int i = 0; i < width * height; i++)
        {
            var (b, g, r, a) = pixel(i);
            dds[128 + i * 4] = b;
            dds[129 + i * 4] = g;
            dds[130 + i * 4] = r;
            dds[131 + i * 4] = a;
        }
        return dds;
    }

    // 16-bit lookup table in a half-float DDS format; value(x, y, z, channel) gives each code.
    public static byte[] LutDds(uint fourCc, int channels, int width, int height, int depth,
        Func<int, int, int, int, ushort> value)
    {
        var header = DdsHeader(width, height, 0x4, fourCc, 0, 0, 0, 0, 0, depth);
        var dds = new byte[128 + width * height * depth * channels * 2];
        header.CopyTo(dds, 0);
        int o = 128;
        for (int z = 0; z < depth; z++)
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    for (int c = 0; c < channels; c++, o += 2)
                        BitConverter.GetBytes(value(x, y, z, c)).CopyTo(dds, o);
        return dds;
    }
}
