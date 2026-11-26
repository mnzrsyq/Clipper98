using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace Clipper98;

public sealed class CaptionPreviewElement : FrameworkElement
{
    private static readonly string[] SampleWords = ["kata", "aktif", "kata", "aktif"];
    private CaptionStyle _style = new();
    private WpfFontFamily _fontFamily = new("Segoe UI");

    public void Update(CaptionStyle style, WpfFontFamily fontFamily)
    {
        _style = style;
        _fontFamily = fontFamily;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        const double scale = 480.0 / 1920.0;
        var maxChars = CaptionLayout.CharactersPerLine(_style.FontSize, _style.LetterSpacing);
        var sampleWords = CaptionTextCase.Transform(string.Join(' ', SampleWords), _style.TextCase)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var groups = CaptionLayout.Group(sampleWords, word => word, maxChars, _style.MaxLines);
        var lines = _style.WordByWord
            ? new List<List<string>> { new() { "AKTIF" } }
            : groups.FirstOrDefault() ?? new List<List<string>> { new() { "INI" } };
        var weight = _style.FontBold ? FontWeights.Bold : FontWeights.Normal;
        var slant = _style.FontItalic ? FontStyles.Italic : FontStyles.Normal;
        var typeface = new Typeface(_fontFamily, slant, weight, FontStretches.Normal);
        var fontSize = _style.FontSize * scale;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tracking = _style.LetterSpacing * scale;
        var primary = PreviewColor(_style.TextColor);
        var highlight = PreviewColor(_style.HighlightColor);
        var outline = _style.Outline > 0
            ? new WpfPen(WpfBrushes.Black, _style.Outline * scale * 2) { LineJoin = PenLineJoin.Round }
            : null;
        outline?.Freeze();

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var words = lines[lineIndex];
            var wordWidths = words.Select(word => tracking == 0
                ? Measure(word, typeface, fontSize, pixelsPerDip)
                : word.Sum(character => Measure(character.ToString(), typeface, fontSize, pixelsPerDip))
                    + Math.Max(0, word.Length - 1) * tracking).ToArray();
            var lineWidth = wordWidths.Sum()
                + Math.Max(0, words.Count - 1) * Measure(" ", typeface, fontSize, pixelsPerDip)
                + Math.Max(0, words.Count - 1) * tracking;
            var x = Math.Max(0, (ActualWidth - lineWidth) / 2);
            var lineCenter = CaptionLayout.LinePositionY(
                _style.PositionY, lineIndex, lines.Count, _style.FontSize, _style.LineSpacing) * scale;

            for (var wordIndex = 0; wordIndex < words.Count; wordIndex++)
            {
                var word = words[wordIndex];
                var isActive = word.Equals(CaptionTextCase.Transform("aktif", _style.TextCase), StringComparison.OrdinalIgnoreCase)
                    && _style.HighlightEnabled;
                if (isActive && _style.HighlightMode == "box")
                {
                    var padding = 5 * scale;
                    var boxHeight = Math.Max(fontSize * 0.92, fontSize * 0.7);
                    var box = new Rect(x - padding, lineCenter - boxHeight / 2, wordWidths[wordIndex] + padding * 2, boxHeight);
                    drawingContext.DrawRoundedRectangle(highlight, null, box, 2.5 * scale, 2.5 * scale);
                }
                var wordBrush = isActive && _style.HighlightMode != "box" ? highlight : primary;
                if (tracking == 0)
                {
                    DrawGlyph(word, x, lineCenter, wordBrush, typeface, fontSize, pixelsPerDip, outline, drawingContext);
                    x += wordWidths[wordIndex];
                }
                else
                {
                    for (var characterIndex = 0; characterIndex < word.Length; characterIndex++)
                    {
                        var glyph = word[characterIndex].ToString();
                        DrawGlyph(glyph, x, lineCenter, wordBrush, typeface, fontSize, pixelsPerDip, outline, drawingContext);
                        x += Measure(glyph, typeface, fontSize, pixelsPerDip);
                        if (characterIndex + 1 < word.Length) x += tracking;
                    }
                }

                if (wordIndex + 1 < words.Count)
                {
                    x += Measure(" ", typeface, fontSize, pixelsPerDip) + tracking;
                }
            }
        }
    }

    private void DrawGlyph(string text, double x, double lineCenter, WpfBrush fill, Typeface typeface,
        double fontSize, double pixelsPerDip, WpfPen? outline, DrawingContext drawingContext)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, WpfFlowDirection.LeftToRight,
            typeface, fontSize, fill, pixelsPerDip);
        var geometry = formatted.BuildGeometry(new WpfPoint(0, 0));
        var bounds = geometry.Bounds;
        if (bounds.IsEmpty) return;
        var y = lineCenter - bounds.Height / 2 - bounds.Top;
        var transform = new TranslateTransform(x, y);
        geometry.Transform = transform;
        if (outline is not null) drawingContext.DrawGeometry(WpfBrushes.Black, outline, geometry);
        drawingContext.DrawGeometry(fill, null, geometry);
    }

    private static double Measure(string text, Typeface typeface, double fontSize, double pixelsPerDip) =>
        new FormattedText(text, CultureInfo.CurrentUICulture, WpfFlowDirection.LeftToRight,
            typeface, fontSize, WpfBrushes.White, pixelsPerDip).WidthIncludingTrailingWhitespace;

    private static WpfBrush PreviewColor(string value)
    {
        var color = value switch
        {
            "Yellow" => WpfColor.FromRgb(255, 211, 41),
            "Pink" => WpfColor.FromRgb(255, 79, 163),
            "Red" => WpfColor.FromRgb(255, 90, 75),
            _ => WpfBrushes.White.Color
        };
        if (value.Length == 7 && value[0] == '#'
            && byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            && byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            && byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            color = WpfColor.FromRgb(red, green, blue);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
