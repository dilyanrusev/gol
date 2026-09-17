using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

/// <summary>The packed cell encoding: both forms round-trip, the choice between them, and the sizes it buys.</summary>
public class CellsCodecTests
{
    private static int[] Sorted(IEnumerable<int> indices) => indices.Distinct().Order().ToArray();

    [Fact]
    public void An_empty_view_is_the_indices_tag_alone()
    {
        var encoded = CellsCodec.Encode([], 100, 100);
        Assert.Equal("I", encoded);
        Assert.Empty(CellsCodec.Decode(encoded, 100, 100));
    }

    [Fact]
    public void Sparse_views_use_delta_coded_indices_and_round_trip()
    {
        var cells = Sorted([0, 1, 2, 4300, 4301, 9999]);

        var encoded = CellsCodec.Encode(cells, 100, 100);

        Assert.Equal(CellsCodec.IndicesTag, encoded[0]);
        Assert.Equal(cells, CellsCodec.Decode(encoded, 100, 100));
        // Six indices, two of them with gaps over 127: eight varint bytes, eleven base64 chars plus the tag.
        Assert.Equal(1 + 12, encoded.Length);
    }

    [Fact]
    public void Dense_views_use_a_bitmap_and_round_trip()
    {
        var random = new Random(7);
        var cells = Sorted(Enumerable.Range(0, 250_000).Where(_ => random.Next(5) == 0)); // about a fifth alive

        var encoded = CellsCodec.Encode(cells, 500, 500);

        Assert.Equal(CellsCodec.BitmapTag, encoded[0]);
        Assert.Equal(cells, CellsCodec.Decode(encoded, 500, 500));
        Assert.Equal(1 + (31_250 + 2) / 3 * 4, encoded.Length); // 250 000 bits, base64
    }

    [Fact]
    public void The_bitmap_takes_over_at_one_cell_in_twelve()
    {
        const int area = 100 * 100;
        // Dense means count * threshold >= area: 834 cells of 10 000 tip it, 833 do not.
        var dense = (area + CellsCodec.DenseThreshold - 1) / CellsCodec.DenseThreshold;
        var justSparse = Enumerable.Range(0, dense - 1).Select(i => i * CellsCodec.DenseThreshold).ToArray();
        var justDense = Enumerable.Range(0, dense).Select(i => i * CellsCodec.DenseThreshold).ToArray();

        Assert.Equal(CellsCodec.IndicesTag, CellsCodec.Encode(justSparse, 100, 100)[0]);
        Assert.Equal(CellsCodec.BitmapTag, CellsCodec.Encode(justDense, 100, 100)[0]);
        Assert.Equal(justSparse, CellsCodec.Decode(CellsCodec.Encode(justSparse, 100, 100), 100, 100));
        Assert.Equal(justDense, CellsCodec.Decode(CellsCodec.Encode(justDense, 100, 100), 100, 100));
    }

    [Fact]
    public void A_full_view_and_the_last_cell_round_trip()
    {
        var full = Enumerable.Range(0, 5 * 7).ToArray();
        Assert.Equal(full, CellsCodec.Decode(CellsCodec.Encode(full, 5, 7), 5, 7));
        Assert.Equal(new[] { 34 }, CellsCodec.Decode(CellsCodec.Encode([34], 5, 7), 5, 7));
    }

    [Fact]
    public void A_kept_scratch_buffer_makes_encoding_allocate_only_the_string()
    {
        var random = new Random(3);
        var cells = Sorted(Enumerable.Range(0, 250_000).Where(_ => random.Next(3) == 0));
        var scratch = Array.Empty<byte>();
        var warm = CellsCodec.Encode(cells, 500, 500, ref scratch);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var encoded = CellsCodec.Encode(cells, 500, 500, ref scratch);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(warm, encoded);
        var stringBytes = 2L * encoded.Length + 32;
        Assert.True(bytes <= stringBytes, $"encoding allocated {bytes} bytes; the string alone is about {stringBytes}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("Igw")] // a varint that never ends
    [InlineData("BAA")] // a bitmap of the wrong length for 3 x 3
    public void Malformed_input_is_rejected(string encoded)
    {
        Assert.ThrowsAny<FormatException>(() => CellsCodec.Decode(encoded, 3, 3));
    }

    [Fact]
    public void Unsorted_or_out_of_range_indices_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => CellsCodec.Encode([5, 3], 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => CellsCodec.Encode(Enumerable.Range(0, 20).Select(i => i * 6).ToArray(), 10, 10));
    }
}
