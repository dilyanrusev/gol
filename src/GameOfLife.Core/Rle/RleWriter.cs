using System.Globalization;
using System.Text;

namespace GameOfLife.Core.Rle;

/// <summary>Serialises a <see cref="Pattern"/> to RLE text (rule B3/S23, lines wrapped at 70 columns).</summary>
public static class RleWriter
{
    public const int MaxLineLength = 70;

    public static string Write(Pattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(pattern.Name))
            sb.Append("#N ").Append(pattern.Name).Append('\n');
        foreach (var comment in pattern.Comments)
            sb.Append("#C ").Append(comment).Append('\n');
        if (pattern.Origin is { } origin)
            sb.Append("#C ").Append(RleParser.OriginCommentPrefix)
              .Append(origin.X.ToString(CultureInfo.InvariantCulture)).Append(' ')
              .Append(origin.Y.ToString(CultureInfo.InvariantCulture)).Append('\n');

        sb.Append(CultureInfo.InvariantCulture, $"x = {pattern.Width}, y = {pattern.Height}, rule = B3/S23\n");

        var body = new StringBuilder();
        var line = new StringBuilder();

        void Emit(int count, char tag)
        {
            if (count <= 0) return;
            var token = count == 1 ? tag.ToString() : count.ToString(CultureInfo.InvariantCulture) + tag;
            if (line.Length + token.Length > MaxLineLength)
            {
                body.Append(line).Append('\n');
                line.Clear();
            }
            line.Append(token);
        }

        var rows = pattern.Cells
            .GroupBy(c => c.Y)
            .OrderBy(g => g.Key)
            .Select(g => (Y: g.Key, Xs: g.Select(c => c.X).Distinct().OrderBy(x => x).ToArray()));

        int currentY = 0;
        foreach (var (y, xs) in rows)
        {
            Emit(y - currentY, '$');
            currentY = y;

            int x = 0;
            int i = 0;
            while (i < xs.Length)
            {
                Emit(xs[i] - x, 'b');
                int runStart = i;
                while (i + 1 < xs.Length && xs[i + 1] == xs[i] + 1) i++;
                Emit(i - runStart + 1, 'o');
                x = xs[i] + 1;
                i++;
            }
        }

        Emit(1, '!');
        body.Append(line);
        sb.Append(body).Append('\n');
        return sb.ToString();
    }
}
