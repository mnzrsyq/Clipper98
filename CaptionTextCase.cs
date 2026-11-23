namespace Clipper98;

public static class CaptionTextCase
{
    public static List<SubtitleWord> Apply(IReadOnlyList<SubtitleWord> words, string mode)
    {
        var result = new List<SubtitleWord>(words.Count);
        var capitalizeNext = true;

        foreach (var word in words)
        {
            var text = Transform(word.Text, mode, ref capitalizeNext);
            result.Add(new SubtitleWord { Start = word.Start, End = word.End, Text = text });
        }

        return result;
    }

    public static string Transform(string text, string mode)
    {
        var capitalizeNext = true;
        return Transform(text, mode, ref capitalizeNext);
    }

    private static string Transform(string text, string mode, ref bool capitalizeNext)
    {
        switch (mode)
        {
            case "lowercase":
                return text.ToLowerInvariant();
            case "uppercase":
                return text.ToUpperInvariant();
            case "title":
                var title = new System.Text.StringBuilder(text.Length);
                var capitalizeWord = true;
                foreach (var character in text)
                {
                    if (char.IsLetter(character))
                    {
                        title.Append(capitalizeWord ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
                        capitalizeWord = false;
                    }
                    else
                    {
                        title.Append(character);
                        capitalizeWord = char.IsWhiteSpace(character) || character is '-' or '–' or '—';
                    }
                }
                return title.ToString();
            case "toggle":
                var toggled = new System.Text.StringBuilder(text.Length);
                foreach (var character in text)
                    toggled.Append(char.IsUpper(character) ? char.ToLowerInvariant(character)
                        : char.IsLower(character) ? char.ToUpperInvariant(character)
                        : character);
                return toggled.ToString();
            case "sentence":
            default:
                var sentence = new System.Text.StringBuilder(text.Length);
                foreach (var character in text)
                {
                    if (char.IsLetter(character))
                    {
                        sentence.Append(capitalizeNext ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
                        capitalizeNext = false;
                    }
                    else
                    {
                        sentence.Append(character);
                        if (character is '.' or '?' or '!') capitalizeNext = true;
                    }
                }
                return sentence.ToString();
        }
    }
}
