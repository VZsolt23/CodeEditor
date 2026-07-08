using System.Windows;
using CodeEditor.Application.Interfaces;
using CodeEditor.Application.Services;
using CodeEditor.Core.Documents;
using CodeEditor.Infrastructure.FileSystem;
using CodeEditor.Infrastructure.Logging;
using CodeEditor.Infrastructure.Lsp;
using CodeEditor.Infrastructure.Roslyn;
using CodeEditor.Infrastructure.Settings;
using CodeEditor.Infrastructure.Terminal;
using CodeEditor.Infrastructure.Workspace;
using CodeEditor.Services;
using CodeEditor.ViewModels;
using CodeEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodeEditor;

/// <summary>
/// Application entry point and dependency injection composition root.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private ILoggerFactory? _loggerFactory;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The output service must exist before the logger factory so log output
        // can be mirrored into the Output panel's "Log" channel.
        var outputService = new OutputService();
        _loggerFactory = LoggingSetup.CreateLoggerFactory(outputService);
        _logger = _loggerFactory.CreateLogger<App>();
        _logger.LogInformation("Code Editor starting");

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _services = BuildServiceProvider(_loggerFactory, outputService);

        // Settings must be loaded before any ViewModel reads them. Task.Run keeps the
        // whole load off the dispatcher context so blocking here cannot deadlock.
        var settingsService = _services.GetRequiredService<ISettingsService>();
        Task.Run(() => settingsService.LoadAsync()).GetAwaiter().GetResult();

        var themeService = _services.GetRequiredService<IThemeService>();
        themeService.ApplyTheme(settingsService.Settings.Theme);

        // Recolor the .xshd syntax palette to the theme before any editor renders,
        // so files paint on-theme immediately instead of showing light-oriented
        // colors until Roslyn's semantic pass catches up. Re-run on theme change.
        SyntaxHighlightingTheme.Apply();
        themeService.ThemeChanged += (_, _) => SyntaxHighlightingTheme.Apply();

        MainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow.Show();

        // The coordinators must exist before session restore reopens the workspace,
        // or they miss the WorkspaceChanged event that triggers the language loads.
        _services.GetRequiredService<CSharpDiagnosticsCoordinator>();
        _services.GetRequiredService<LspDiagnosticsCoordinator>();
        _services.GetRequiredService<XmlDiagnosticsCoordinator>();

        // Best effort, after the window is up; failures surface per file (or are
        // logged) without blocking startup.
        _ = _services.GetRequiredService<MainViewModel>().RestoreSessionAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            var settingsService = _services?.GetService<ISettingsService>();
            if (settingsService is not null)
            {
                Task.Run(() => settingsService.SaveAsync()).GetAwaiter().GetResult();
            }
            _logger?.LogInformation("Code Editor exited");
        }
        finally
        {
            _services?.Dispose();
            _loggerFactory?.Dispose();
        }

        base.OnExit(e);
    }

    private static ServiceProvider BuildServiceProvider(ILoggerFactory loggerFactory, IOutputService outputService)
    {
        var services = new ServiceCollection();

        // Logging
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // Core
        services.AddSingleton<ILanguageRegistry, LanguageRegistry>();

        // Application
        services.AddSingleton<IRecentFilesService, RecentFilesService>();
        services.AddSingleton(outputService);

        // Infrastructure
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<ITerminalService, TerminalService>();
        services.AddSingleton<ICodeAnalysisService, RoslynWorkspaceService>();
        services.AddSingleton<ILspService, LspService>();

        // Presentation
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<CSharpDiagnosticsCoordinator>();
        services.AddSingleton<LspDiagnosticsCoordinator>();
        services.AddSingleton<XmlDiagnosticsCoordinator>();
        services.AddSingleton<EditorOptionsViewModel>();
        services.AddSingleton<DocumentsViewModel>();
        services.AddSingleton<ExplorerViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<FindReplaceViewModel>();
        services.AddSingleton<OutputViewModel>();
        services.AddSingleton<ProblemsViewModel>();
        services.AddSingleton<TerminalViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled exception");

        var dialogService = _services?.GetService<IDialogService>();
        dialogService?.ShowError("Unexpected Error", e.Exception.Message);
        e.Handled = true;
    }
}
