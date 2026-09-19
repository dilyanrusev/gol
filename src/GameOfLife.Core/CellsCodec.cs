using System.Buffers;

namespace GameOfLife.Core;

/// <summary>
/// Packs a viewport's visible cells into a few bytes for the wire: one tag byte followed by the
/// payload. Sparse views use <c>I</c>, the sorted indices delta-coded as LEB128 varints (about one
/// byte per cell when cells are close together); dense views use <c>B</c>, a bitmap with one bit
/// per viewport cell, row-major, least significant bit first (a fixed width × height / 8 bytes
/// whatever the population). The choice is made per frame, so the payload is bounded by the bitmap
/// in the worst case and near-minimal in the common, mostly empty one. MessagePack carries the
/// bytes as they are; a JSON client sees them base64-encoded.
/// </summary>
public static class CellsCodec
{
    public const byte IndicesTag = (byte)'I';
    public const byte BitmapTag = (byte)'B';

    /// <summary>The bitmap is used once at least one cell in this many is alive.</summary>
    public const int DenseThreshold = 12;

    /// <summary>
    /// Encodes <paramref name="indices"/> (sorted ascending, each in [0, width × height)) into a
    /// new array: the tag byte, then the payload. <paramref name="scratch"/> holds the payload while
    /// it is built and is grown when needed, so a caller that keeps it allocates only the result.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<int> indices, int width, int height, ref byte[] scratch)
    {
        var area = checked(width * height);
        var dense = (long)indices.Length * DenseThreshold >= area;
        var length = dense ? EncodeBitmap(indices, area, ref scratch) : EncodeIndices(indices, ref scratch);
        var encoded = new byte[1 + length];
        encoded[0] = dense ? BitmapTag : IndicesTag;
        scratch.AsSpan(0, length).CopyTo(encoded.AsSpan(1));
        return encoded;
    }

    /// <summary>Convenience for one-off frames: encodes with a temporary scratch buffer.</summary>
    public static byte[] Encode(ReadOnlySpan<int> indices, int width, int height)
    {
        var scratch = Array.Empty<byte>();
        return Encode(indices, width, height, ref scratch);
    }

    /// <summary>Decodes bytes produced by <see cref="Encode(ReadOnlySpan{int}, int, int, ref byte[])"/> back into sorted indices.</summary>
    public static int[] Decode(ReadOnlySpan<byte> encoded, int width, int height)
    {
        if (encoded.Length == 0) throw new FormatException("The encoded cells are empty; a tag byte is required.");
        var payload = encoded[1..];
        return encoded[0] switch
        {
            IndicesTag => DecodeIndices(payload, checked(width * height)),
            BitmapTag => DecodeBitmap(payload, checked(width * height)),
            var tag => throw new FormatException($"Unknown cell encoding '{(char)tag}'."),
        };
    }

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

    private static int[] DecodeIndices(ReadOnlySpan<byte> bytes, int area)
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

    private static int[] DecodeBitmap(ReadOnlySpan<byte> bytes, int area)
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
