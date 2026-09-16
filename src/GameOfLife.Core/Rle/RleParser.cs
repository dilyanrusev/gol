using System.Globalization;
using System.Text.RegularExpressions;

namespace GameOfLife.Core.Rle;

/// <summary>
/// Parses Run Length Encoded (RLE) Life patterns as described at
/// https://conwaylife.com/wiki/Run_Length_Encoded. Only rule B3/S23 is accepted.
/// </summary>
public static partial class RleParser
{
    public const string OriginCommentPrefix = "origin ";

    [GeneratedRegex(@"^\s*x\s*=\s*(?<x>\d+)\s*,\s*y\s*=\s*(?<y>\d+)\s*(,\s*rule\s*=\s*(?<rule>[^\s,]+)\s*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderRegex();

    private static readonly HashSet<string> AcceptedRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "B3/S23", "23/3", "S23/B3",
    };

    public static Pattern Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        return Parse(reader);
    }

    public static Pattern Parse(TextReader reader)
    {
        string? name = null;
        Cell? origin = null;
        var comments = new List<string>();
        int? headerWidth = null, headerHeight = null;
        var cells = new List<(int X, int Y)>();

        int x = 0, y = 0, run = 0;
        bool finished = false;
        int lineNumber = 0;

        string? line;
        while (!finished && (line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            if (line.StartsWith('#'))
            {
                if (headerWidth is not null)
                    throw new FormatException($"Line {lineNumber}: comment lines must precede the header.");
                ParseCommentLine(line, ref name, comments, ref origin);
                continue;
            }

            if (headerWidth is null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var m = HeaderRegex().Match(line);
                if (!m.Success)
                    throw new FormatException($"Line {lineNumber}: expected a header like 'x = 3, y = 3, rule = B3/S23'.");

                headerWidth = int.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture);
                headerHeight = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
                if (headerWidth > Pattern.MaxDimension || headerHeight > Pattern.MaxDimension)
                    throw new FormatException($"Line {lineNumber}: pattern size exceeds {Pattern.MaxDimension}.");

                if (m.Groups["rule"].Success && !AcceptedRules.Contains(m.Groups["rule"].Value))
                    throw new FormatException(
                        $"Line {lineNumber}: unsupported rule '{m.Groups["rule"].Value}'. Only B3/S23 (Conway's Life) is supported.");
                continue;
            }

            foreach (var ch in line)
            {
                if (char.IsWhiteSpace(ch)) continue;

                if (char.IsAsciiDigit(ch))
                {
                    run = checked(run * 10 + (ch - '0'));
                    if (run > Pattern.MaxDimension)
                        throw new FormatException($"Line {lineNumber}: run length {run} exceeds {Pattern.MaxDimension}.");
                    continue;
                }

                int count = run == 0 ? 1 : run;
                run = 0;

                if (ch == '!')
                {
                    finished = true;
                    break;
                }

                if (ch == '$')
                {
                    y += count;
                    x = 0;
                    continue;
                }

                if (ch == 'b' || ch == 'B')
                {
                    x += count;
                    continue;
                }

                if (!char.IsAsciiLetter(ch))
                    throw new FormatException($"Line {lineNumber}: unexpected character '{ch}'.");

                // 'o' is the canonical live cell; other letters are treated as live too
                // (multi-state files use A..X), matching common Life software.
                if (x + count > Pattern.MaxDimension || y >= Pattern.MaxDimension)
                    throw new FormatException($"Line {lineNumber}: pattern exceeds the {Pattern.MaxDimension} size limit.");
                for (var i = 0; i < count; i++) cells.Add((x + i, y));
                x += count;
            }
        }

        if (headerWidth is null)
            throw new FormatException("The RLE header line ('x = ..., y = ...') is missing.");

        int actualWidth = cells.Count == 0 ? 0 : cells.Max(c => c.X) + 1;
        int actualHeight = cells.Count == 0 ? 0 : cells.Max(c => c.Y) + 1;

        return new Pattern
        {
            Width = Math.Max(headerWidth.Value, actualWidth),
            Height = Math.Max(headerHeight!.Value, actualHeight),
            Cells = cells.Distinct().OrderBy(c => c.Y).ThenBy(c => c.X).ToArray(),
            Name = name,
            Comments = comments,
            Origin = origin,
        };
    }

    private static void ParseCommentLine(string line, ref string? name, List<string> comments, ref Cell? origin)
    {
        if (line.Length < 2) return;
        char kind = line[1];
        string body = line.Length > 2 ? line[2..].Trim() : string.Empty;

        switch (kind)
        {
            case 'N':
                name = body;
                break;
            case 'C':
            case 'c':
                if (body.StartsWith(OriginCommentPrefix, StringComparison.OrdinalIgnoreCase) && TryParseOrigin(body, out var parsed))
                    origin = parsed;
                else
                    comments.Add(body);
                break;
            case 'O':
                comments.Add("Author: " + body);
                break;
            default:
                // #P, #R, #r and unknown kinds are ignored.
                break;
        }
    }

    private static bool TryParseOrigin(string body, out Cell origin)
    {
        origin = default;
        var parts = body[OriginCommentPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        if (!ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ox)) return false;
        if (!ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var oy)) return false;
        origin = new Cell(ox, oy);
        return true;
    }
}
