namespace CodeEditor.Application.Services;

/// <summary>
/// The language ids served by the LSP backend (TypeScript/JavaScript, HTML, CSS, JSON).
/// These match <c>LanguageRegistry</c> ids, which double as LSP languageId values.
/// Single source of truth for routing editor requests to the language server.
/// </summary>
public static class LspLanguages
{
    private static readonly HashSet<string> Ids = new(StringComparer.Ordinal)
    {
        "typescript",
        "typescriptreact",
        "javascript",
        "javascriptreact",
        "html",
        "css",
        "scss",
        "less",
        "json",
        "jsonc",
    };

    /// <summary>Whether documents of <paramref name="languageId"/> are served by the LSP backend.</summary>
    public static bool Includes(string languageId) => Ids.Contains(languageId);
}
