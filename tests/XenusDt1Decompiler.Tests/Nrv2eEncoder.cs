namespace XenusDt1Decompiler.Tests;

// Minimal UCL NRV2E encoder (8-bit bit buffer) used to produce method 1..3 test streams.
// It mirrors the decoder: literals, matches with a new offset, and the end-of-stream marker.
// Matches are found greedily with a naive search, which is enough for small test inputs.
internal static class Nrv2eEncoder
{
    public static byte[] Encode(byte[] data)
    {
        var w = new BitWriter();
        int pos = 0;
        while (pos < data.Length)
        {
            var (off, len) = FindMatch(data, pos);
            if (len >= 2)
            {
                WriteMatch(w, off, len);
                pos += len;
            }
            else
            {
                w.Bit(1);
                w.Byte(data[pos++]);
            }
        }

        // End of stream: a match whose offset prefix decodes to 0xFFFFFFFF.
        w.Bit(0);
        WriteOffsetPrefix(w, 0x1000002);
        w.Byte(0xFF);
        return w.ToArray();
    }

    private static (int off, int len) FindMatch(byte[] data, int pos)
    {
        int bestLen = 0, bestOff = 0;
        int maxLen = Math.Min(255, data.Length - pos);
        for (int off = 1; off <= Math.Min(pos, 0x2000); off++)
        {
            int len = 0;
            while (len < maxLen && data[pos + len] == data[pos - off + len])
                len++;
            int minLen = off > 0x500 ? 3 : 2; // far matches cost one extra length unit
            if (len >= minLen && len > bestLen)
            {
                bestLen = len;
                bestOff = off;
            }
        }
        return (bestOff, bestLen);
    }

    private static void WriteMatch(BitWriter w, int off, int count)
    {
        // Decoder copies (len + 1) bytes, where len is the coded length plus 1 for far offsets.
        int len = count - 1 - (off > 0x500 ? 1 : 0);
        bool shortLen = len is 1 or 2;

        uint raw = ((uint)(off - 1) << 1) | (shortLen ? 0u : 1u);
        w.Bit(0);
        WriteOffsetPrefix(w, (raw >> 8) + 3);
        w.Byte((byte)raw);

        if (shortLen)
        {
            w.Bit(len - 1);
        }
        else if (len is 3 or 4)
        {
            w.Bit(1);
            w.Bit(len - 3);
        }
        else
        {
            w.Bit(0);
            WriteGamma(w, (uint)(len - 3));
        }
    }

    // Offset prefix: the decoder starts at 1 and repeats off = 2*off + b1, then either stops
    // (bit 1) or continues with off = 2*(off - 1) + b2.
    private static void WriteOffsetPrefix(BitWriter w, uint value)
    {
        foreach (int bit in Reach(value >> 1))
            w.Bit(bit);
        w.Bit((int)(value & 1));
        w.Bit(1);
    }

    private static List<int> Reach(uint target)
    {
        if (target == 1)
            return new List<int>();
        uint t = target + 2;
        var bits = Reach(t >> 2);
        bits.Add((int)((t >> 1) & 1));
        bits.Add(0);
        bits.Add((int)(t & 1));
        return bits;
    }

    // Length gamma code: value >= 2, bits after the leading 1, each followed by a stop flag.
    private static void WriteGamma(BitWriter w, uint value)
    {
        int top = 31 - System.Numerics.BitOperations.LeadingZeroCount(value);
        for (int i = top - 1; i >= 0; i--)
        {
            w.Bit((int)((value >> i) & 1));
            w.Bit(i == 0 ? 1 : 0);
        }
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _out = new();
        private int _bitPos = -1;
        private int _bitCount = 8;

        public void Bit(int bit)
        {
            if (_bitCount == 8)
            {
                _bitPos = _out.Count;
                _out.Add(0);
                _bitCount = 0;
            }
            if (bit != 0)
                _out[_bitPos] |= (byte)(0x80 >> _bitCount);
            _bitCount++;
        }

        public void Byte(byte b) => _out.Add(b);

        public byte[] ToArray() => _out.ToArray();
    }
}
