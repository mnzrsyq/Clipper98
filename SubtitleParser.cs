using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;

namespace Clipper98;

/// <summary>Membaca file subtitle SRT/VTT menjadi daftar cue.</summary>
public static class SubtitleParser
{
    public static List<SubtitleCue> TrimToRange(IEnumerable<SubtitleCue> cues, double clipStart, double clipEnd)
    {
        var result = new List<SubtitleCue>();
        foreach (var cue in cues.OrderBy(cue => cue.Start))
        {
            var start = Math.Max(cue.Start, clipStart);
            var end = Math.Min(cue.End, clipEnd);
            if (end <= start || string.IsNullOrWhiteSpace(cue.Text)) continue;
            result.Add(new SubtitleCue
            {
                Start = start - clipStart,
                End = end - clipStart,
                Text = cue.Text.Trim()
            });
        }
        return result;
    }

    public static List<SubtitleCue> ParseWordJson(string file)
    {
        if (!File.Exists(file)) return [];

        using var document = JsonDocument.Parse(File.ReadAllText(file));
        if (!document.RootElement.TryGetProperty("transcription", out var segments) || segments.ValueKind != JsonValueKind.Array)
            return [];

        var cues = new List<SubtitleCue>();
        foreach (var segment in segments.EnumerateArray())
        {
            if (!segment.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array) continue;
            var words = new List<SubtitleWord>();
            var current = new StringBuilder();
            var wordStart = 0d;
            var wordEnd = 0d;

            void FlushWord()
            {
                var text = current.ToString().Trim();
                if (text.Length > 0 && wordEnd >= wordStart)
                    words.Add(new SubtitleWord { Start = wordStart, End = wordEnd, Text = text });
                current.Clear();
            }

            foreach (var token in tokens.EnumerateArray())
            {
                if (!token.TryGetProperty("text", out var textNode) || textNode.ValueKind != JsonValueKind.String) continue;
                var tokenText = textNode.GetString() ?? string.Empty;
                if (tokenText.Length == 0 || tokenText.StartsWith('[')) continue;
                if (!token.TryGetProperty("offsets", out var offsets)
                    || !offsets.TryGetProperty("from", out var fromNode)
                    || !offsets.TryGetProperty("to", out var toNode)
                    || !fromNode.TryGetDouble(out var fromMs)
                    || !toNode.TryGetDouble(out var toMs)) continue;

                var beginsWord = char.IsWhiteSpace(tokenText[0]);
                if (beginsWord && current.Length > 0) FlushWord();
                if (current.Length == 0) wordStart = fromMs / 1000d;
                current.Append(tokenText.Trim());
                wordEnd = toMs / 1000d;
            }
            FlushWord();

            if (words.Count == 0) continue;
            var text = string.Join(" ", words.Select(word => word.Text));
            cues.Add(new SubtitleCue
            {
                Start = words[0].Start,
                End = words[^1].End,
                Text = text,
                Words = words
            });
        }
        return cues;
    }

    public static List<SubtitleCue> ParseFile(string file)
    {
        if (!File.Exists(file)) return [];

        var raw = System.Net.WebUtility.HtmlDecode(File.ReadAllText(file).Replace("\r\n", "\n"));
        raw = Regex.Replace(raw, "<[^>]+>", string.Empty);

        var cues = new List<SubtitleCue>();
        foreach (var block in Regex.Split(raw, @"\n\s*\n"))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var timingIndex = Array.FindIndex(lines, line => line.Contains("-->"));
            if (timingIndex < 0) continue;

            var range = lines[timingIndex].Split("-->", StringSplitOptions.TrimEntries);
            if (range.Length != 2) continue;
            if (!TryParseTime(range[0], out var start) || !TryParseTime(range[1].Split(' ')[0], out var end)) continue;

            var text = string.Join(" ", lines.Skip(timingIndex + 1)).Trim();
            if (text.Length > 0) cues.Add(new SubtitleCue { Start = start, End = end, Text = text });
        }
        return cues;
    }

    /// <summary>Menerima HH:MM:SS,mmm / HH:MM:SS.mmm / MM:SS.mmm.</summary>
    private static bool TryParseTime(string input, out double seconds)
    {
        seconds = 0;
        var parts = input.Trim().Replace(',', '.').Split(':');
        if (parts.Length is < 2 or > 3) return false;

        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
            total = total * 60 + value;
        }
        seconds = total;
        return true;
    }
}
