using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace Clipper98;

public static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["FaceBrush"] = "#C0C0C0",
        ["WindowBrush"] = "#ECE9D8",
        ["TitleBrush"] = "#000080",
        ["FocusBrush"] = "#0066CC",
        ["AccentBrush"] = "#D19B00",
        ["AccentTextBrush"] = "#111111",
        ["ErrorBrush"] = "#B42318",
        ["TextBrush"] = "#111111",
        ["MutedTextBrush"] = "#454545",
        ["InputBrush"] = "#FFFFFF",
        ["InputTextBrush"] = "#111111",
        ["BorderBrush"] = "#808080",
        ["HoverBrush"] = "#E8E8E8",
        ["PressedBrush"] = "#9D9D9D",
        ["SelectedBrush"] = "#D6D3CE"
    };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["FaceBrush"] = "#292929",
        ["WindowBrush"] = "#1E1E1E",
        ["TitleBrush"] = "#101038",
        ["FocusBrush"] = "#8AB4F8",
        ["AccentBrush"] = "#B89432",
        ["AccentTextBrush"] = "#111111",
        ["ErrorBrush"] = "#FF8A80",
        ["TextBrush"] = "#E8E8E8",
        ["MutedTextBrush"] = "#B5B5B5",
        ["InputBrush"] = "#151515",
        ["InputTextBrush"] = "#F1F1F1",
        ["BorderBrush"] = "#747474",
        ["HoverBrush"] = "#3A3A3A",
        ["PressedBrush"] = "#171717",
        ["SelectedBrush"] = "#444444"
    };

    public static void Apply(bool nightMode)
    {
        var palette = nightMode ? DarkPalette : LightPalette;
        foreach (var entry in palette)
        {
            var color = (WpfColor)WpfColorConverter.ConvertFromString(entry.Value)!;
            WpfApplication.Current.Resources[entry.Key] = new SolidColorBrush(color);
        }

        // Set SystemColors brushes so popups, ComboBox dropdowns, and native controls don't show white-on-white
        var windowColor = (WpfColor)WpfColorConverter.ConvertFromString(nightMode ? "#1E1E1E" : "#FFFFFF")!;
        var windowTextColor = (WpfColor)WpfColorConverter.ConvertFromString(nightMode ? "#E8E8E8" : "#111111")!;
        var controlColor = (WpfColor)WpfColorConverter.ConvertFromString(nightMode ? "#292929" : "#C0C0C0")!;

        WpfApplication.Current.Resources[System.Windows.SystemColors.WindowBrushKey] = new SolidColorBrush(windowColor);
        WpfApplication.Current.Resources[System.Windows.SystemColors.WindowTextBrushKey] = new SolidColorBrush(windowTextColor);
        WpfApplication.Current.Resources[System.Windows.SystemColors.ControlBrushKey] = new SolidColorBrush(controlColor);
        WpfApplication.Current.Resources[System.Windows.SystemColors.ControlTextBrushKey] = new SolidColorBrush(windowTextColor);
        WpfApplication.Current.Resources[System.Windows.SystemColors.HighlightBrushKey] = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(nightMode ? "#335588" : "#0A246A")!);
        WpfApplication.Current.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = new SolidColorBrush(Colors.White);
    }
}
