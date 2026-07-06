namespace CodeEditor.Core.Completion;

/// <summary>
/// Overload information for the call the caret is inside.
/// </summary>
/// <param name="Signatures">Display signatures of the candidate overloads.</param>
/// <param name="ActiveSignature">Index of the overload that best matches the argument count.</param>
/// <param name="ActiveParameter">0-based index of the argument the caret is on.</param>
public sealed record SignatureHelpInfo(IReadOnlyList<string> Signatures, int ActiveSignature, int ActiveParameter);
