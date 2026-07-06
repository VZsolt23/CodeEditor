using System.Windows;

namespace CodeEditor.Views;

/// <summary>
/// Simple themed single-line text prompt used for explorer operations
/// (new file, new folder, rename).
/// </summary>
public partial class InputDialog : Window
{
    public InputDialog(string title, string prompt, string? initialValue)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue ?? string.Empty;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            SelectFileNameStem(initialValue);
        };
    }

    /// <summary>The text entered by the user; meaningful only when the dialog returned true.</summary>
    public string Value => ValueBox.Text;

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;

    /// <summary>Selects the name without its extension so renames can overtype just the stem.</summary>
    private void SelectFileNameStem(string? initialValue)
    {
        if (string.IsNullOrEmpty(initialValue))
        {
            return;
        }

        var stemLength = initialValue.LastIndexOf('.');
        ValueBox.Select(0, stemLength > 0 ? stemLength : initialValue.Length);
    }
}
