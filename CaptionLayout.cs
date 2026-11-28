namespace Clipper98;

public static class CaptionLayout
{
    public const double SafeWidth = 920;

    public const double HighlightBoxExtraWidth = 3;
    public const double HighlightBoxExtraHeight = 2;

    public static int CharactersPerLine(int fontSize, double letterSpacing) =>
        Math.Clamp((int)(SafeWidth / Math.Max(1, fontSize * 0.62 + letterSpacing)), 10, 40);

    public static int LinePositionY(int centerY, int lineIndex, int lineCount, int fontSize, int lineSpacing)
    {
        var lineHeight = fontSize + lineSpacing;
        var offset = (lineIndex - (lineCount - 1) / 2.0) * lineHeight;
        return Math.Clamp((int)Math.Round(centerY + offset), 160, 1840);
    }

    public static List<List<List<T>>> Group<T>(
        IReadOnlyList<T> items, Func<T, string> textSelector, int charactersPerLine, int maxLines)
    {
        var groups = new List<List<List<T>>>();
        var lines = new List<List<T>> { new() };
        var lineLength = 0;
        var lineCount = 1;
        maxLines = Math.Clamp(maxLines, 1, 4);

        foreach (var item in items)
        {
            var wordLength = textSelector(item).Length;
            var separatorLength = lineLength > 0 ? 1 : 0;
            if (lineLength > 0 && lineLength + separatorLength + wordLength > charactersPerLine)
            {
                if (lineCount >= maxLines)
                {
                    if (lines[^1].Count > 0) groups.Add(lines);
                    lines = [new List<T>()];
                    lineCount = 1;
                }
                else
                {
                    lines.Add(new List<T>());
                    lineCount++;
                }
                lineLength = 0;
                separatorLength = 0;
            }

            lines[^1].Add(item);
            lineLength += separatorLength + wordLength;
        }

        if (lines.Count > 0 && lines[^1].Count > 0) groups.Add(lines);
        return groups;
    }
}
