namespace CodeEditor.Core.Documents;

/// <summary>
/// Default <see cref="ILanguageRegistry"/> implementation pre-populated with the
/// languages the editor targets. Thread-safe for concurrent reads after startup registration.
/// </summary>
public sealed class LanguageRegistry : ILanguageRegistry
{
    private readonly List<LanguageInfo> _languages = [];
    private readonly Dictionary<string, LanguageInfo> _byExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public LanguageRegistry()
    {
        foreach (var language in CreateDefaultLanguages())
        {
            Register(language);
        }
    }

    public IReadOnlyList<LanguageInfo> Languages
    {
        get
        {
            lock (_gate)
            {
                return [.. _languages];
            }
        }
    }

    public void Register(LanguageInfo language)
    {
        ArgumentNullException.ThrowIfNull(language);

        lock (_gate)
        {
            _languages.RemoveAll(existing => existing.Id == language.Id);
            _languages.Add(language);

            foreach (var extension in language.Extensions)
            {
                _byExtension[extension] = language;
            }
        }
    }

    public LanguageInfo GetForFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath);
        if (extension.Length == 0)
        {
            return LanguageInfo.PlainText;
        }

        lock (_gate)
        {
            return _byExtension.TryGetValue(extension, out var language)
                ? language
                : LanguageInfo.PlainText;
        }
    }

    private static IEnumerable<LanguageInfo> CreateDefaultLanguages()
    {
        yield return new LanguageInfo("csharp", "C#", [".cs", ".csx"]);
        yield return new LanguageInfo("html", "HTML", [".html", ".htm", ".cshtml", ".razor"]);
        yield return new LanguageInfo("css", "CSS", [".css"]);
        yield return new LanguageInfo("scss", "SCSS", [".scss"]);
        yield return new LanguageInfo("less", "Less", [".less"]);
        yield return new LanguageInfo("javascript", "JavaScript", [".js", ".mjs", ".cjs"]);
        yield return new LanguageInfo("javascriptreact", "JavaScript React", [".jsx"]);
        yield return new LanguageInfo("typescript", "TypeScript", [".ts", ".mts", ".cts"]);
        yield return new LanguageInfo("typescriptreact", "TypeScript React", [".tsx"]);
        // Separate ids: the JSON language server flags comments under "json" but
        // accepts them under "jsonc", so each needs its correct LSP languageId.
        yield return new LanguageInfo("json", "JSON", [".json"]);
        yield return new LanguageInfo("jsonc", "JSON with Comments", [".jsonc"]);
        yield return new LanguageInfo("xml", "XML",
            [".xml", ".csproj", ".props", ".targets", ".config", ".xaml", ".resx", ".nuspec", ".xsd", ".xslt"]);
        yield return new LanguageInfo("yaml", "YAML", [".yml", ".yaml"]);
        yield return new LanguageInfo("markdown", "Markdown", [".md", ".markdown"]);
        yield return new LanguageInfo("solution", "Solution", [".sln", ".slnx"]);
        yield return LanguageInfo.PlainText;
    }
}
