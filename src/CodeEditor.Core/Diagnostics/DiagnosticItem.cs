namespace CodeEditor.Core.Diagnostics;

/// <summary>Severity of a diagnostic, ordered from least to most severe.</summary>
public enum DiagnosticSeverity
{
    /// <summary>An editorial hint (e.g. a naming suggestion).</summary>
    Hint,

    /// <summary>Informational message.</summary>
    Info,

    /// <summary>A potential problem that does not prevent building.</summary>
    Warning,

    /// <summary>An error.</summary>
    Error,
}

/// <summary>
/// One entry in the Problems panel. Produced by language services (Roslyn/LSP)
/// in Phases 4–5.
/// </summary>
/// <param name="Severity">How severe the problem is.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="FilePath">Absolute path of the affected file, or null when not file-bound.</param>
/// <param name="Line">1-based line of the problem (0 when unknown).</param>
/// <param name="Column">1-based column of the problem (0 when unknown).</param>
/// <param name="Source">Producer of the diagnostic (e.g. "csharp", "typescript").</param>
/// <param name="Length">Character length of the affected span (0 when unknown; renderers underline at least one character).</param>
public sealed record DiagnosticItem(
    DiagnosticSeverity Severity,
    string Message,
    string? FilePath,
    int Line,
    int Column,
    string Source,
    int Length = 0);
