using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodeEditor.Core.Diagnostics;
using CodeEditor.ViewModels;

namespace CodeEditor.Views;

/// <summary>
/// Main application window. Pure view logic lives here: panel collapse/restore
/// (grid metrics are not bindable-friendly) and the async close-confirmation flow.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    private bool _closeApproved;
    private double _sidebarWidth = 260;
    private double _bottomPanelHeight = 180;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.ExitRequested += (_, _) => Close();
        viewModel.FindInFilesRequested += (_, _) => FocusSearchPanel();
        viewModel.SearchResultsRequested += (_, _) => SidebarTabs.SelectedItem = SearchTab;
        viewModel.Find.FocusFindRequested += (_, _) => FocusFindBox();
        viewModel.Find.PropertyChanged += OnFindPanelPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseWindowClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        // With custom chrome, a maximized window overhangs the screen edges by the
        // resize border; compensate so no content is clipped.
        RootLayout.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_closeApproved)
        {
            // Re-entry after the async close-all flow; the session was already
            // captured on the first pass (the tabs are gone by now).
            return;
        }

        _viewModel.CaptureSessionState();

        if (!_viewModel.Documents.HasDirtyDocuments)
        {
            return;
        }

        // Save prompts are async; cancel this close and re-issue it once resolved.
        e.Cancel = true;
        _ = ConfirmAndCloseAsync();
    }

    private async Task ConfirmAndCloseAsync()
    {
        if (await _viewModel.Documents.TryCloseAllAsync())
        {
            _closeApproved = true;
            Close();
        }
    }

    /// <summary>Keeps the Output panel scrolled to the newest line.</summary>
    private void OnOutputTextChanged(object sender, TextChangedEventArgs e) => OutputTextBox.ScrollToEnd();

    /// <summary>Keeps the Terminal panel scrolled to the newest output.</summary>
    private void OnTerminalTextChanged(object sender, TextChangedEventArgs e) => TerminalOutputBox.ScrollToEnd();

    /// <summary>Double-click on a problem navigates to its location.</summary>
    private void OnProblemsListMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && ProblemsList.SelectedItem is DiagnosticItem item)
        {
            _viewModel.Problems.OpenDiagnosticCommand.Execute(item);
            e.Handled = true;
        }
    }

    /// <summary>Enter on a problem navigates to its location.</summary>
    private void OnProblemsListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ProblemsList.SelectedItem is DiagnosticItem item)
        {
            _viewModel.Problems.OpenDiagnosticCommand.Execute(item);
            e.Handled = true;
        }
    }

    /// <summary>Puts keyboard focus into the find box once the panel has rendered.</summary>
    private void FocusFindBox()
    {
        Dispatcher.InvokeAsync(() =>
        {
            FindTextBox.SelectAll();
            FindTextBox.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Returns focus to the editor when the find panel closes.</summary>
    private void OnFindPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FindReplaceViewModel.IsVisible)
            && !_viewModel.Find.IsVisible
            && _viewModel.Documents.ActiveDocument is not null)
        {
            FindDescendant<EditorView>(DocumentTabs)?.FocusEditor();
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>Switches the sidebar to the search tab and focuses the query box.</summary>
    private void FocusSearchPanel()
    {
        SidebarTabs.SelectedItem = SearchTab;

        // Focus after the tab's content has rendered, or focus lands nowhere.
        Dispatcher.InvokeAsync(() =>
        {
            SearchQueryBox.SelectAll();
            SearchQueryBox.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Enter in the search results opens the selected match or toggles a file group.</summary>
    private void OnSearchResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        switch (SearchResultsTree.SelectedItem)
        {
            case SearchMatchViewModel match:
                match.OpenCommand.Execute(null);
                e.Handled = true;
                break;
            case SearchFileResultViewModel file:
                file.IsExpanded = !file.IsExpanded;
                e.Handled = true;
                break;
        }
    }

    /// <summary>Opens the double-clicked explorer file; folder expansion stays native.</summary>
    private void OnExplorerTreeMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left
            && GetTreeItem(e.OriginalSource) is { IsDirectory: false } item)
        {
            item.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Enter in the explorer opens the selected file or toggles the selected folder.</summary>
    private void OnExplorerTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ExplorerTree.SelectedItem is not FileTreeItemViewModel item)
        {
            return;
        }

        if (item.IsDirectory)
        {
            item.IsExpanded = !item.IsExpanded;
        }
        else
        {
            item.OpenCommand.Execute(null);
        }

        e.Handled = true;
    }

    private static FileTreeItemViewModel? GetTreeItem(object source)
    {
        var element = source as DependencyObject;
        while (element is not null and not TreeViewItem)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return (element as TreeViewItem)?.DataContext as FileTreeItemViewModel;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsSidebarVisible):
                UpdateSidebarVisibility();
                break;
            case nameof(MainViewModel.IsBottomPanelVisible):
                UpdateBottomPanelVisibility();
                break;
        }
    }

    private void UpdateSidebarVisibility()
    {
        if (_viewModel.IsSidebarVisible)
        {
            SidebarColumn.MinWidth = 150;
            SidebarColumn.Width = new GridLength(_sidebarWidth);
            SidebarHost.Visibility = Visibility.Visible;
            SidebarSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            _sidebarWidth = SidebarColumn.ActualWidth;
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(0);
            SidebarHost.Visibility = Visibility.Collapsed;
            SidebarSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateBottomPanelVisibility()
    {
        if (_viewModel.IsBottomPanelVisible)
        {
            BottomPanelRow.MinHeight = 80;
            BottomPanelRow.Height = new GridLength(_bottomPanelHeight);
            BottomPanelHost.Visibility = Visibility.Visible;
            BottomSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            _bottomPanelHeight = BottomPanelRow.ActualHeight;
            BottomPanelRow.MinHeight = 0;
            BottomPanelRow.Height = new GridLength(0);
            BottomPanelHost.Visibility = Visibility.Collapsed;
            BottomSplitter.Visibility = Visibility.Collapsed;
        }
    }
}
