namespace CodeEditor.Core.Documents;

/// <summary>
/// Central registry of languages known to the editor. Languages can be registered
/// at startup (or later by extensions) and are resolved by file extension.
/// </summary>
public interface ILanguageRegistry
{
    /// <summary>All registered languages.</summary>
    IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>Registers an additional language. Extensions already claimed by another language are re-mapped.</summary>
    void Register(LanguageInfo language);

    /// <summary>Resolves the language for a file path, falling back to <see cref="LanguageInfo.PlainText"/>.</summary>
    LanguageInfo GetForFile(string filePath);
}
