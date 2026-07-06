namespace CodeEditor.Application.Interfaces;

/// <summary>Outcome of a save-changes confirmation prompt.</summary>
public enum ConfirmationResult
{
    /// <summary>Save the document, then continue.</summary>
    Save,

    /// <summary>Discard changes and continue.</summary>
    Discard,

    /// <summary>Abort the operation that triggered the prompt.</summary>
    Cancel,
}

/// <summary>
/// Abstraction over user-facing dialogs so ViewModels never touch UI framework types directly.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows an open-file dialog. Returns the selected path, or null if cancelled.</summary>
    string? ShowOpenFileDialog();

    /// <summary>Shows a save-file dialog. Returns the selected path, or null if cancelled.</summary>
    string? ShowSaveFileDialog(string? suggestedFileName = null);

    /// <summary>Shows a folder picker. Returns the selected directory path, or null if cancelled.</summary>
    string? ShowOpenFolderDialog();

    /// <summary>Prompts the user for a single line of text. Returns the entered text, or null if cancelled.</summary>
    string? ShowInputDialog(string title, string prompt, string? initialValue = null);

    /// <summary>Asks the user a yes/no question. Returns true if the user confirmed.</summary>
    bool Confirm(string title, string message);

    /// <summary>Asks the user whether unsaved changes in <paramref name="documentName"/> should be saved.</summary>
    ConfirmationResult ConfirmSaveChanges(string documentName);

    /// <summary>Shows an error message.</summary>
    void ShowError(string title, string message);

    /// <summary>Shows an informational message.</summary>
    void ShowInformation(string title, string message);
}
