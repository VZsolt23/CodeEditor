# CLAUDE.md

WPF code editor for Windows (VS Code-inspired, deliberately simpler). C# / .NET, targets `net10.0-windows`.

## Read First

- [docs/STATUS.md](docs/STATUS.md) — what's done (Phases 1–2), what's next (Phase 3: workspace explorer, find-in-files, bottom panels), full backlog, known quirks
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layering, ViewModel structure, theme system contract, conventions
- [docs/SPEC.md](docs/SPEC.md) — the original project specification (requirements, phase roadmap, code style rules)

## Commands

```powershell
dotnet build CodeEditor.slnx          # NOTE: .slnx (XML solution format), there is no .sln
dotnet run --project src/CodeEditor
```

Smoke test for shutdown correctness: launch the exe, `taskkill /PID <pid>` (no `/F`); the log at `%APPDATA%\CodeEditor\logs\` must end with "Code Editor exited" and `%APPDATA%\CodeEditor\settings.json` must be rewritten.

## Hard Rules

- Layering is strict: ViewModels depend on `CodeEditor.Application` interfaces, never on Infrastructure implementations or on WPF-specific services directly.
- Fully qualify `System.Windows.Application` in the WPF project (the `CodeEditor.Application` namespace shadows it).
- `App.OnStartup`/`OnExit` block on async settings I/O: everything on those paths needs `ConfigureAwait(false)`, **including `await using` disposals** (`await using (stream.ConfigureAwait(false))`). This has caused a real shutdown deadlock before.
- `EditorView` is one instance reused across all tabs — per-document state lives on `DocumentViewModel`, never in the view.
- New theme = resource dictionary defining **every** brush key from `src/CodeEditor/Themes/DarkTheme.xaml` + registration in `ThemeService`. All colors in views/styles must be `DynamicResource`.
- Ensure `dotnet build CodeEditor.slnx` succeeds (0 warnings is the current baseline) before considering a change done.

## Style

- CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`) for ViewModels.
- Nullable enabled solution-wide; XML docs on public APIs; small focused methods; async/await with meaningful exception filters (`catch (Exception ex) when (ex is IOException or ...)`).
