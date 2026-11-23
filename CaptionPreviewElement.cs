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
    private static readonly string[] SampleWords = ["KATA", "AKTIF", "KATA", "AKTIF"];
    private CaptionStyle _style = new();
    private WpfFontFamily _fontFamily = new("Montserrat");

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
        var groups = CaptionLayout.Group(SampleWords, word => word, maxChars, _style.MaxLines);
        var lines = groups.FirstOrDefault() ?? [["INI"]];
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
                if (tracking == 0)
                {
                    DrawGlyph(word, x, lineCenter, word == "AKTIF" && _style.HighlightEnabled ? highlight : primary,
                        typeface, fontSize, pixelsPerDip, outline, drawingContext);
                    x += wordWidths[wordIndex];
                }
                else
                {
                    for (var characterIndex = 0; characterIndex < word.Length; characterIndex++)
                    {
                        var glyph = word[characterIndex].ToString();
                        DrawGlyph(glyph, x, lineCenter, word == "AKTIF" && _style.HighlightEnabled ? highlight : primary,
                            typeface, fontSize, pixelsPerDip, outline, drawingContext);
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

    private static WpfBrush PreviewColor(string value) => value switch
    {
        "Yellow" => new SolidColorBrush(WpfColor.FromRgb(255, 211, 41)),
        "Cyan" => new SolidColorBrush(WpfColor.FromRgb(70, 230, 235)),
        "Pink" => new SolidColorBrush(WpfColor.FromRgb(255, 79, 163)),
        "Red" => new SolidColorBrush(WpfColor.FromRgb(255, 90, 75)),
        _ => WpfBrushes.White
    };
}
