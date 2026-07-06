using System.IO;
using ICSharpCode.AvalonEdit.Highlighting;

namespace CodeEditor.Services;

/// <summary>
/// Resolves AvalonEdit syntax highlighting definitions for file paths, including
/// mappings for extensions AvalonEdit does not know natively (TypeScript, JSX,
/// MSBuild files, Razor). Semantic language services replace this in later phases.
/// </summary>
public static class EditorHighlighting
{
    /// <summary>Returns the highlighting definition for the file, or null for plain text.</summary>
    public static IHighlightingDefinition? GetDefinition(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        var manager = HighlightingManager.Instance;
        var extension = Path.GetExtension(filePath);

        var definition = manager.GetDefinitionByExtension(extension);
        if (definition is not null)
        {
            return definition;
        }

        return extension.ToLowerInvariant() switch
        {
            ".ts" or ".tsx" or ".jsx" or ".mjs" or ".cjs" or ".mts" or ".cts"
                => manager.GetDefinition("JavaScript"),
            ".json" or ".jsonc"
                => manager.GetDefinition("Json") ?? manager.GetDefinition("JavaScript"),
            ".csproj" or ".props" or ".targets" or ".config" or ".xaml" or ".resx"
                or ".nuspec" or ".xsd" or ".xslt"
                => manager.GetDefinition("XML"),
            ".cshtml" or ".razor"
                => manager.GetDefinition("HTML"),
            _ => null,
        };
    }
}
