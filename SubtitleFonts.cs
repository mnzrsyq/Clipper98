using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace Clipper98;

public sealed class SubtitleFontOption
{
    public string DisplayName { get; init; } = string.Empty;
    public string FamilyName { get; init; } = string.Empty;
    public string FaceName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public bool IsImported { get; init; }
    public bool IsBoldFace { get; init; }
    public bool IsItalicFace { get; init; }
}

public static class SubtitleFonts
{
    private static readonly Dictionary<string, IReadOnlyList<WpfFontFamily>> LocalFamilyCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string UserFontsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipper98", "Fonts");

    public static IReadOnlyList<SubtitleFontOption> LoadOptions()
    {
        var options = new List<SubtitleFontOption>();
        foreach (var family in Fonts.SystemFontFamilies.OrderBy(GetFamilyName, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = GetFamilyName(family);
            if (string.IsNullOrWhiteSpace(name)) continue;
            options.Add(new SubtitleFontOption { DisplayName = name, FamilyName = name });
        }

        var localFonts = Path.Combine(AppContext.BaseDirectory, "tools", "fonts");
        AddLocalFonts(options, localFonts, imported: false);
        AddBundledFallback(options, localFonts, "Montserrat", "Montserrat[wght].ttf", bold: false);
        AddBundledFallback(options, localFonts, "Poppins", "Poppins-ExtraBold.ttf", bold: true);
        AddLocalFonts(options, UserFontsFolder, imported: true);
        return options
            .GroupBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string Import(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pilih file font .ttf atau .otf.");

        _ = ReadMetadata(sourcePath); // Pastikan file dikenali sebagai font sebelum menyalinnya.
        Directory.CreateDirectory(UserFontsFolder);
        using var stream = File.OpenRead(sourcePath);
        var hash = Convert.ToHexString(SHA256.HashData(stream))[..12].ToLowerInvariant();
        var safeName = string.Concat(Path.GetFileNameWithoutExtension(sourcePath)
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var destination = Path.Combine(UserFontsFolder, $"{safeName}-{hash}{extension.ToLowerInvariant()}");
        if (!File.Exists(destination)) File.Copy(sourcePath, destination);
        LocalFamilyCache.Clear();
        return destination;
    }

    public static WpfFontFamily ResolvePreviewFamily(SubtitleFontOption? option)
    {
        if (option is null) return new WpfFontFamily("Montserrat");
        if (!string.IsNullOrWhiteSpace(option.FilePath) && File.Exists(option.FilePath))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(option.FilePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                var family = GetLocalFamilies(directory).FirstOrDefault(candidate =>
                    string.Equals(GetFamilyName(candidate), option.FamilyName, StringComparison.OrdinalIgnoreCase));
                if (family is not null) return family;
            }
        }
        return new WpfFontFamily(option.FamilyName);
    }

    private static void AddLocalFonts(List<SubtitleFontOption> options, string directory, bool imported)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory)
                     .Where(path => Path.GetExtension(path).Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                                 || Path.GetExtension(path).Equals(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var metadata = ReadMetadata(path);
                var label = imported
                    ? $"{metadata.Family} — {metadata.Face} (impor)"
                    : metadata.Family;
                options.Add(new SubtitleFontOption
                {
                    DisplayName = label,
                    FamilyName = metadata.Family,
                    FaceName = metadata.Face,
                    FilePath = path,
                    IsImported = imported,
                    IsBoldFace = metadata.Bold,
                    IsItalicFace = metadata.Italic
                });
            }
            catch
            {
                // Skip files that WPF cannot inspect as a usable font.
            }
        }
    }

    private static void AddBundledFallback(List<SubtitleFontOption> options, string directory, string family, string fileName, bool bold)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path) || options.Any(option =>
                string.Equals(option.FilePath, path, StringComparison.OrdinalIgnoreCase))) return;
        options.Add(new SubtitleFontOption
        {
            DisplayName = family,
            FamilyName = family,
            FaceName = bold ? "ExtraBold" : "Regular",
            FilePath = path,
            IsBoldFace = bold
        });
    }

    private static (string Family, string Face, bool Bold, bool Italic) ReadMetadata(string path)
    {
        var glyph = new GlyphTypeface(new Uri(Path.GetFullPath(path)));
        var culture = CultureInfo.CurrentUICulture;
        var family = GetLocalized(glyph.FamilyNames, culture);
        var face = GetLocalized(glyph.FaceNames, culture);
        var bold = glyph.Weight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();
        var italic = glyph.Style == FontStyles.Italic || glyph.Style == FontStyles.Oblique;
        return (family, face, bold, italic);
    }

    private static string GetLocalized(IEnumerable<KeyValuePair<CultureInfo, string>> values, CultureInfo culture)
    {
        var entries = values.ToList();
        return entries.FirstOrDefault(item => Equals(item.Key, culture)).Value
            ?? entries.FirstOrDefault().Value
            ?? string.Empty;
    }

    private static string GetFamilyName(WpfFontFamily family)
    {
        var names = family.FamilyNames;
        return names[XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.Name)]
            ?? names.Values.Cast<string>().FirstOrDefault()
            ?? family.Source;
    }

    private static IReadOnlyList<WpfFontFamily> GetLocalFamilies(string directory)
    {
        var key = Path.GetFullPath(directory);
        if (LocalFamilyCache.TryGetValue(key, out var cached)) return cached;

        var uri = new Uri(key.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var families = Fonts.GetFontFamilies(uri).ToList();
        LocalFamilyCache[key] = families;
        return families;
    }
}
