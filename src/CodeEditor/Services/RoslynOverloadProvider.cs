using System.ComponentModel;
using CodeEditor.Core.Completion;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace CodeEditor.Services;

/// <summary>
/// Feeds AvalonEdit's <see cref="OverloadInsightWindow"/> with the overload
/// signatures of a <see cref="SignatureHelpInfo"/>; Up/Down cycles overloads.
/// </summary>
public sealed class RoslynOverloadProvider : IOverloadProvider
{
    private readonly SignatureHelpInfo _info;
    private int _selectedIndex;

    public RoslynOverloadProvider(SignatureHelpInfo info)
    {
        _info = info;
        _selectedIndex = Math.Clamp(info.ActiveSignature, 0, Math.Max(0, info.Signatures.Count - 1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count => _info.Signatures.Count;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = Math.Clamp(value, 0, Math.Max(0, Count - 1));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentIndexText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentHeader)));
        }
    }

    public string CurrentIndexText => $"{SelectedIndex + 1} of {Count}";

    public object CurrentHeader => _info.Signatures[SelectedIndex];

    public object? CurrentContent => null;
}
