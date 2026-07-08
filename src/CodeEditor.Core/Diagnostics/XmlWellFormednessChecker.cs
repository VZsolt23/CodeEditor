using System.Xml;

namespace CodeEditor.Core.Diagnostics;

/// <summary>
/// Checks XML text for well-formedness. Parsing stops at the first fatal error
/// (XML errors are fatal by specification), so at most one diagnostic is
/// produced. DTDs are ignored and no external resources are ever resolved.
/// </summary>
public static class XmlWellFormednessChecker
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        ConformanceLevel = ConformanceLevel.Document,
    };

    /// <summary>
    /// Parses <paramref name="text"/> and returns the first well-formedness error,
    /// or an empty list when the document parses (or is blank).
    /// </summary>
    public static IReadOnlyList<DiagnosticItem> Check(string? filePath, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            using var reader = XmlReader.Create(new StringReader(text), ReaderSettings);
            while (reader.Read())
            {
            }

            return [];
        }
        catch (XmlException ex)
        {
            return
            [
                new DiagnosticItem(
                    DiagnosticSeverity.Error,
                    ex.Message,
                    filePath,
                    ex.LineNumber,
                    ex.LinePosition,
                    Source: "xml"),
            ];
        }
    }
}
