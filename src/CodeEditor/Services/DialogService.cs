using System.Windows;
using CodeEditor.Application.Interfaces;
using CodeEditor.Views;
using Microsoft.Win32;

namespace CodeEditor.Services;

/// <summary>
/// WPF implementation of <see cref="IDialogService"/> using the common dialogs
/// and message boxes, parented to the main window when available.
/// </summary>
public sealed class DialogService : IDialogService
{
    private const string FileFilter =
        "All Files (*.*)|*.*" +
        "|C# Files (*.cs)|*.cs" +
        "|Web Files (*.html;*.css;*.js;*.ts;*.jsx;*.tsx)|*.html;*.htm;*.css;*.scss;*.less;*.js;*.ts;*.jsx;*.tsx" +
        "|JSON Files (*.json)|*.json;*.jsonc" +
        "|XML and Project Files (*.xml;*.csproj;*.props;*.targets;*.config)|*.xml;*.csproj;*.props;*.targets;*.config;*.xaml;*.resx" +
        "|Markdown Files (*.md)|*.md;*.markdown" +
        "|YAML Files (*.yml;*.yaml)|*.yml;*.yaml" +
        "|Text Files (*.txt)|*.txt";

    public string? ShowOpenFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = FileFilter,
            CheckFileExists = true,
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    public string? ShowSaveFileDialog(string? suggestedFileName = null)
    {
        var dialog = new SaveFileDialog
        {
            Filter = FileFilter,
            FileName = suggestedFileName ?? string.Empty,
            AddExtension = false,
        };

        return dialog.ShowDialog(GetOwner()) == true ? dialog.FileName : null;
    }

    public string? ShowOpenFolderDialog()
    {
        var dialog = new OpenFolderDialog();
        return dialog.ShowDialog(GetOwner()) == true ? dialog.FolderName : null;
    }

    public string? ShowInputDialog(string title, string prompt, string? initialValue = null)
    {
        var dialog = new InputDialog(title, prompt, initialValue) { Owner = GetOwner() };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public bool Confirm(string title, string message)
        => ShowMessageBox(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public ConfirmationResult ConfirmSaveChanges(string documentName)
    {
        var result = ShowMessageBox(
            $"Do you want to save the changes you made to '{documentName}'?\n\nYour changes will be lost if you don't save them.",
            "Code Editor",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => ConfirmationResult.Save,
            MessageBoxResult.No => ConfirmationResult.Discard,
            _ => ConfirmationResult.Cancel,
        };
    }

    public void ShowError(string title, string message)
        => ShowMessageBox(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInformation(string title, string message)
        => ShowMessageBox(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static Window? GetOwner()
    {
        var mainWindow = System.Windows.Application.Current?.MainWindow;
        return mainWindow?.IsLoaded == true ? mainWindow : null;
    }

    private static MessageBoxResult ShowMessageBox(
        string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = GetOwner();
        return owner is not null
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
    }
}
