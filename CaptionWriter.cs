using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Clipper98;

/// <summary>Membuat file caption ASS (gaya, pemecahan baris panjang, highlight kata).</summary>
public static class CaptionWriter
{
    /// <summary>
    /// Memecah cue panjang menjadi potongan pendek (sekitar 2 baris) supaya teks tidak menutupi video.
    /// Waktu dibagi sebanding dengan jumlah karakter tiap potongan.
    /// </summary>
    public static List<SubtitleCue> SplitLongCues(IEnumerable<SubtitleCue> cues, int fontSize)
    {
        var maxChars = CaptionLayout.CharactersPerLine(fontSize, 0) * 2;
        var result = new List<SubtitleCue>();

        foreach (var cue in cues)
        {
            var chunks = ChunkWords(cue.Text, maxChars);
            if (chunks.Count <= 1)
            {
                result.Add(cue);
                continue;
            }

            var totalChars = chunks.Sum(chunk => chunk.Length);
            var span = Math.Max(0.1, cue.End - cue.Start);
            var cursor = cue.Start;
            foreach (var chunk in chunks)
            {
                var length = span * chunk.Length / totalChars;
                result.Add(new SubtitleCue { Start = cursor, End = cursor + length, Text = chunk });
                cursor += length;
            }
        }
        return result;
    }

    private static List<string> ChunkWords(string text, int maxChars)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }

    public static void WriteAss(string file, IEnumerable<SubtitleCue> cues, CaptionStyle style)
    {
        using var writer = new StreamWriter(file, false, Encoding.UTF8);
        writer.WriteLine("[Script Info]");
        writer.WriteLine("ScriptType: v4.00+");
        writer.WriteLine("PlayResX: 1080");
        writer.WriteLine("PlayResY: 1920");
        writer.WriteLine("[V4+ Styles]");
        writer.WriteLine("Format: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding");
        var fontName = style.FontName.Replace(",", " ").Trim();
        var spacing = style.LetterSpacing.ToString("0.##", CultureInfo.InvariantCulture);
        writer.WriteLine($"Style: Default,{fontName},{style.FontSize},{AssColor(style.TextColor)},&H00000000,&H00000000,&H00000000,{(style.FontBold ? -1 : 0)},{(style.FontItalic ? -1 : 0)},0,0,100,100,{spacing},0,1,{style.Outline},0,5,80,80,0,1");
        writer.WriteLine("[Events]");
        writer.WriteLine("Format: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text");
        var maxCharsPerLine = CaptionLayout.CharactersPerLine(style.FontSize, style.LetterSpacing);
        var maxLines = Math.Clamp(style.MaxLines, 1, 4);
        var positionY = Math.Clamp(style.PositionY, 160, 1840);
        var cueWords = cues.OrderBy(cue => cue.Start)
            .Select(cue => cue.Words.Count > 0
                ? cue.Words.OrderBy(word => word.Start).ThenBy(word => word.End).ToList()
                : EstimateWordTimings(cue))
            .Where(words => words.Count > 0)
            .ToList();
        var convertedWords = CaptionTextCase.Apply(cueWords.SelectMany(words => words).ToList(), style.TextCase);
        var blocks = new List<CaptionBlock>();
        var wordOffset = 0;
        foreach (var rawWords in cueWords)
        {
            var words = convertedWords.Skip(wordOffset).Take(rawWords.Count).ToList();
            wordOffset += rawWords.Count;
            var groups = CaptionLayout.Group(words, word => word.Text, maxCharsPerLine, maxLines);
            foreach (var lines in groups)
            {
                var groupWords = lines.SelectMany(line => line).ToList();
                blocks.Add(new CaptionBlock(lines, groupWords));
            }
        }

        // Source subtitle cues can overlap. Schedule every word globally so the old cue
        // finishes as the next cue starts instead of drawing both phrases on top of each other.
        var scheduledWords = blocks.SelectMany(block => block.Words)
            .OrderBy(word => word.Start)
            .ThenBy(word => word.End)
            .ToList();
        var wordStarts = new Dictionary<SubtitleWord, double>();
        var previousStart = double.NegativeInfinity;
        foreach (var word in scheduledWords)
        {
            var start = Math.Max(0, word.Start);
            if (start < previousStart + 0.02) start = previousStart + 0.02;
            wordStarts[word] = start;
            previousStart = start;
        }
        var nextWordStarts = new Dictionary<SubtitleWord, double?>();
        for (var i = 0; i < scheduledWords.Count; i++)
            nextWordStarts[scheduledWords[i]] = i + 1 < scheduledWords.Count
                ? wordStarts[scheduledWords[i + 1]]
                : null;

        foreach (var block in blocks)
        {
            var lines = block.Lines;
            var groupWords = block.Words;
            var groupEnd = VisibleEnd(groupWords[^1], nextWordStarts[groupWords[^1]], wordStarts[groupWords[^1]]);
            if (!style.HighlightEnabled)
            {
                for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    var line = lines[lineIndex];
                    var text = FormatLine(line, null, style.HighlightColor);
                    var rowY = CaptionLayout.LinePositionY(positionY, lineIndex, lines.Count, style.FontSize, style.LineSpacing);
                    writer.WriteLine($"Dialogue: 0,{AssTime(wordStarts[groupWords[0]])},{AssTime(groupEnd)},Default,,0,0,0,,{{\\pos(540,{rowY})}}{text}");
                }
                continue;
            }

            for (var wordIndex = 0; wordIndex < groupWords.Count; wordIndex++)
            {
                var word = groupWords[wordIndex];
                var start = wordStarts[word];
                var end = VisibleEnd(word, nextWordStarts[word], start);
                for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    var line = lines[lineIndex];
                    var text = FormatLine(line, word, style.HighlightColor);
                    var rowY = CaptionLayout.LinePositionY(positionY, lineIndex, lines.Count, style.FontSize, style.LineSpacing);
                    writer.WriteLine($"Dialogue: 0,{AssTime(start)},{AssTime(end)},Default,,0,0,0,,{{\\pos(540,{rowY})}}{text}");
                }
            }
        }
    }

    private sealed record CaptionBlock(List<List<SubtitleWord>> Lines, List<SubtitleWord> Words);

    private static string FormatLine(IReadOnlyList<SubtitleWord> line, SubtitleWord? highlightedWord, string highlightColor)
    {
        var result = new StringBuilder();
        for (var i = 0; i < line.Count; i++)
        {
            if (i > 0) result.Append(' ');
            var word = line[i].Text;

            var escaped = AssEscape(word);
            if (ReferenceEquals(line[i], highlightedWord))
                result.Append($"{{\\c{AssColor(highlightColor)}&}}{escaped}{{\\c}}");
            else
                result.Append(escaped);
        }
        return result.ToString();
    }

    private static List<SubtitleWord> EstimateWordTimings(SubtitleCue cue)
    {
        var tokens = Regex.Matches(cue.Text, @"\S+").Select(match => match.Value).ToList();
        var duration = cue.End - cue.Start;
        if (tokens.Count == 0 || duration <= 0) return [];

        var totalWeight = tokens.Sum(token => Math.Max(1, token.Length));
        var elapsedWeight = 0;
        var words = new List<SubtitleWord>(tokens.Count);
        foreach (var token in tokens)
        {
            var start = cue.Start + duration * elapsedWeight / totalWeight;
            elapsedWeight += Math.Max(1, token.Length);
            var end = cue.Start + duration * elapsedWeight / totalWeight;
            words.Add(new SubtitleWord { Start = start, End = Math.Max(start + 0.03, end), Text = token });
        }
        return words;
    }

    private static double VisibleEnd(SubtitleWord word, double? nextStart, double start)
    {
        var end = word.End + 0.18;
        if (nextStart is { } next && next > start) end = Math.Min(end, next);
        return Math.Max(start + 0.01, end);
    }

    private static string AssTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture);

    private static string AssColor(string color) => color switch
    {
        "Yellow" => "&H0029D3FF",
        "Cyan" => "&H00EBE646",
        "Pink" => "&H00A34FFF",
        "Red" => "&H004B5AFF",
        _ => "&H00FFFFFF"
    };

    private static string AssEscape(string text) =>
        text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\r", " ").Replace("\n", "\\N");

}
