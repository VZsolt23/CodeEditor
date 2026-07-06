using System.IO;
using System.Windows.Media;

namespace CodeEditor.Services;

/// <summary>
/// Maps file names to Devicon glyphs and brand colors for the explorer file
/// icons. Codepoints index into the bundled Devicon font (Assets/Fonts/devicon.ttf,
/// family "devicon"; see DEVICON-LICENSE.txt). Names without a mapping fall back
/// to a generic themed glyph in the tree template.
/// </summary>
public static class FileIconCatalog
{
    /// <summary>WPF FontFamily reference for the bundled Devicon font.</summary>
    public const string FontFamily = "/CodeEditor;component/Assets/Fonts/#devicon";

    private readonly record struct Icon(int Codepoint, string Color);

    // Whole-name matches (checked before extension) for files identified by name.
    private static readonly Dictionary<string, Icon> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dockerfile"] = new(0xE9C3, "#2496ED"),
        ["package.json"] = new(0xED9E, "#539E43"),
        ["package-lock.json"] = new(0xED9E, "#539E43"),
        [".gitignore"] = new(0xEA2D, "#F05032"),
        [".gitattributes"] = new(0xEA2D, "#F05032"),
        [".gitmodules"] = new(0xEA2D, "#F05032"),
    };

    private static readonly Dictionary<string, Icon> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = new(0xE9A0, "#9B4F96"),
        [".csx"] = new(0xE9A0, "#9B4F96"),
        [".csproj"] = new(0xE9C9, "#8657D4"),
        [".props"] = new(0xE9C9, "#8657D4"),
        [".targets"] = new(0xE9C9, "#8657D4"),
        [".sln"] = new(0xE9C9, "#8657D4"),
        [".slnx"] = new(0xE9C9, "#8657D4"),
        [".js"] = new(0xEA81, "#F0DB4F"),
        [".mjs"] = new(0xEA81, "#F0DB4F"),
        [".cjs"] = new(0xEA81, "#F0DB4F"),
        [".ts"] = new(0xEC63, "#3178C6"),
        [".mts"] = new(0xEC63, "#3178C6"),
        [".cts"] = new(0xEC63, "#3178C6"),
        [".jsx"] = new(0xEBBC, "#61DAFB"),
        [".tsx"] = new(0xEBBC, "#61DAFB"),
        [".json"] = new(0xEA94, "#CBCB41"),
        [".jsonc"] = new(0xEA94, "#CBCB41"),
        [".html"] = new(0xEA67, "#E34F26"),
        [".htm"] = new(0xEA67, "#E34F26"),
        [".cshtml"] = new(0xEA67, "#E34F26"),
        [".razor"] = new(0xEA67, "#E34F26"),
        [".css"] = new(0xE9A1, "#3D8FC6"),
        [".scss"] = new(0xEBEE, "#CC6699"),
        [".sass"] = new(0xEBEE, "#CC6699"),
        [".less"] = new(0xEAC6, "#438EBA"),
        [".xml"] = new(0xECB4, "#E37933"),
        [".xaml"] = new(0xECB4, "#E37933"),
        [".resx"] = new(0xECB4, "#E37933"),
        [".xsd"] = new(0xECB4, "#E37933"),
        [".xslt"] = new(0xECB4, "#E37933"),
        [".config"] = new(0xECB4, "#E37933"),
        [".nuspec"] = new(0xECB4, "#E37933"),
        [".md"] = new(0xEADB, "#4C86C6"),
        [".markdown"] = new(0xEADB, "#4C86C6"),
        [".yml"] = new(0xECB5, "#CB171E"),
        [".yaml"] = new(0xECB5, "#CB171E"),
        [".py"] = new(0xEB9C, "#4B8BBE"),
        [".pyw"] = new(0xEB9C, "#4B8BBE"),
        [".java"] = new(0xEA7F, "#E76F00"),
        [".go"] = new(0xEA3D, "#00ADD8"),
        [".rb"] = new(0xEBE3, "#CC342D"),
        [".php"] = new(0xEB68, "#8892BF"),
        [".sh"] = new(0xED55, "#4EAA25"),
        [".bash"] = new(0xED55, "#4EAA25"),
        [".zsh"] = new(0xED55, "#4EAA25"),
        [".ps1"] = new(0xEB7D, "#5391FE"),
        [".psm1"] = new(0xEB7D, "#5391FE"),
        [".psd1"] = new(0xEB7D, "#5391FE"),
        [".vue"] = new(0xEC96, "#41B883"),
    };

    private static readonly Dictionary<string, Brush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the Devicon glyph and brand-color brush for <paramref name="fileName"/>.
    /// Returns false when there is no mapping (the caller shows a generic glyph).
    /// Call on the UI thread (explorer nodes are created there).
    /// </summary>
    public static bool TryResolve(string fileName, out string glyph, out Brush brush)
    {
        if (!ByName.TryGetValue(fileName, out var icon))
        {
            var extension = Path.GetExtension(fileName);
            if (extension.Length == 0 || !ByExtension.TryGetValue(extension, out icon))
            {
                glyph = string.Empty;
                brush = Brushes.Transparent;
                return false;
            }
        }

        glyph = char.ConvertFromUtf32(icon.Codepoint);
        brush = GetBrush(icon.Color);
        return true;
    }

    private static Brush GetBrush(string hex)
    {
        if (!BrushCache.TryGetValue(hex, out var brush))
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            BrushCache[hex] = brush;
        }

        return brush;
    }
}
