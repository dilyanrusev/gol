using System.Buffers;

namespace GameOfLife.Core;

/// <summary>
/// Packs a viewport's visible cells into a short string for the wire: one tag character followed
/// by base64. Sparse views use <c>I</c>, the sorted indices delta-coded as LEB128 varints (about
/// one byte per cell when cells are close together); dense views use <c>B</c>, a bitmap with one
/// bit per viewport cell, row-major, least significant bit first (a fixed width × height / 8
/// bytes whatever the population). The choice is made per frame, so the payload is bounded by the
/// bitmap in the worst case and near-minimal in the common, mostly empty one.
/// </summary>
public static class CellsCodec
{
    public const char IndicesTag = 'I';
    public const char BitmapTag = 'B';

    /// <summary>The bitmap is used once at least one cell in this many is alive.</summary>
    public const int DenseThreshold = 12;

    /// <summary>
    /// Encodes <paramref name="indices"/> (sorted ascending, each in [0, width × height)) into a
    /// string. <paramref name="scratch"/> holds the bytes before base64 and is grown when needed, so
    /// a caller that keeps it allocates only the returned string.
    /// </summary>
    public static string Encode(ReadOnlySpan<int> indices, int width, int height, ref byte[] scratch)
    {
        var area = checked(width * height);
        var dense = (long)indices.Length * DenseThreshold >= area;
        var length = dense ? EncodeBitmap(indices, area, ref scratch) : EncodeIndices(indices, ref scratch);
        var tag = dense ? BitmapTag : IndicesTag;
        var bytes = new ReadOnlyMemory<byte>(scratch, 0, length);
        return string.Create(1 + Base64Length(length), (tag, bytes), static (chars, state) =>
        {
            chars[0] = state.tag;
            Convert.TryToBase64Chars(state.bytes.Span, chars[1..], out _);
        });
    }

    /// <summary>Convenience for one-off frames: encodes with a temporary scratch buffer.</summary>
    public static string Encode(ReadOnlySpan<int> indices, int width, int height)
    {
        var scratch = Array.Empty<byte>();
        return Encode(indices, width, height, ref scratch);
    }

    /// <summary>Decodes a string produced by <see cref="Encode(ReadOnlySpan{int}, int, int, ref byte[])"/> back into sorted indices.</summary>
    public static int[] Decode(string encoded, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length == 0) throw new FormatException("The encoded cells are empty; a tag character is required.");
        var bytes = Convert.FromBase64String(encoded[1..]);
        return encoded[0] switch
        {
            IndicesTag => DecodeIndices(bytes, checked(width * height)),
            BitmapTag => DecodeBitmap(bytes, checked(width * height)),
            var tag => throw new FormatException($"Unknown cell encoding '{tag}'."),
        };
    }

    private static int Base64Length(int bytes) => (bytes + 2) / 3 * 4;

    private static void Ensure(ref byte[] scratch, int length)
    {
        if (scratch.Length < length) Array.Resize(ref scratch, Math.Max(length, Math.Max(256, scratch.Length * 2)));
    }

    private static int EncodeIndices(ReadOnlySpan<int> indices, ref byte[] scratch)
    {
        Ensure(ref scratch, indices.Length * 5);
        var position = 0;
        var previous = 0;
        foreach (var index in indices)
        {
            if (index < previous) throw new ArgumentException("Indices must be sorted ascending.", nameof(indices));
            var delta = (uint)(index - previous);
            previous = index;
            while (delta >= 0x80)
            {
                scratch[position++] = (byte)(delta | 0x80);
                delta >>= 7;
            }
            scratch[position++] = (byte)delta;
        }
        return position;
    }

    private static int EncodeBitmap(ReadOnlySpan<int> indices, int area, ref byte[] scratch)
    {
        var length = (area + 7) / 8;
        Ensure(ref scratch, length);
        scratch.AsSpan(0, length).Clear();
        foreach (var index in indices)
        {
            if ((uint)index >= (uint)area) throw new ArgumentOutOfRangeException(nameof(indices), index, "Index outside the viewport.");
            scratch[index >> 3] |= (byte)(1 << (index & 7));
        }
        return length;
    }

    private static int[] DecodeIndices(byte[] bytes, int area)
    {
        var writer = new ArrayBufferWriter<int>();
        var previous = 0;
        var position = 0;
        while (position < bytes.Length)
        {
            uint delta = 0;
            var shift = 0;
            byte b;
            do
            {
                if (position == bytes.Length) throw new FormatException("Truncated varint.");
                b = bytes[position++];
                delta |= (uint)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            var index = previous + (int)delta;
            if ((uint)index >= (uint)area) throw new FormatException("Index outside the viewport.");
            writer.GetSpan(1)[0] = index;
            writer.Advance(1);
            previous = index;
        }
        return writer.WrittenSpan.ToArray();
    }

    private static int[] DecodeBitmap(byte[] bytes, int area)
    {
        if (bytes.Length != (area + 7) / 8) throw new FormatException("Bitmap length does not match the viewport.");
        var writer = new ArrayBufferWriter<int>();
        for (var index = 0; index < area; index++)
        {
            if ((bytes[index >> 3] & (1 << (index & 7))) == 0) continue;
            writer.GetSpan(1)[0] = index;
            writer.Advance(1);
        }
        return writer.WrittenSpan.ToArray();
    }
}
