# Code Editor

A modern, lightweight code editor for Windows, inspired by Visual Studio Code and JetBrains Rider but intentionally simpler. Focused on excellent support for modern .NET and web development (C#, TypeScript/JavaScript, HTML, CSS, React, Angular, JSON, XML, YAML, Markdown).

Built with **WPF**, **MVVM**, **Dependency Injection**, **AvalonEdit**, and (in upcoming phases) **Roslyn** and the **Language Server Protocol**.

## Requirements

- Windows 10/11
- .NET SDK 10.0+ (the app targets `net10.0-windows`)

## Build and Run

> Note: the solution file is `CodeEditor.slnx` (the newer XML solution format), **not** `.sln`.

```powershell
dotnet build CodeEditor.slnx          # build everything
dotnet run --project src/CodeEditor   # run the editor
```

Binaries land in `src/CodeEditor/bin/Debug/net10.0-windows/CodeEditor.exe`.

## User Data

| What | Where |
|---|---|
| Settings (JSON) | `%APPDATA%\CodeEditor\settings.json` |
| Logs (rolling, 7 days) | `%APPDATA%\CodeEditor\logs\codeeditor-YYYYMMDD.log` |

Settings can be edited in-app via **File → Settings** (opens the JSON file as a document). Changes made on disk take effect on next start; live reload is on the roadmap.

## Current Feature Set (Phases 1–2 complete)

- Tabbed editing with AvalonEdit: syntax highlighting, line numbers, current-line highlight, undo/redo, auto-indentation (C#-aware), word wrap, zoom (menu, `Ctrl`+`±`, `Ctrl`+wheel)
- File lifecycle: New / Open / Save / Save As / Save All / Close with dirty-state prompts, recent files, optional auto-save
- Dark and Light themes, switchable at runtime (View → Theme), persisted across sessions
- Menu bar, toolbar, status bar (line/column, tab width, language), collapsible sidebar and bottom panel (placeholders until Phase 3)

See [docs/STATUS.md](docs/STATUS.md) for the detailed status and remaining roadmap, [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the codebase is organized, and [docs/SPEC.md](docs/SPEC.md) for the original project specification.

## Keyboard Shortcuts

| Action | Shortcut |
|---|---|
| New file | `Ctrl+N` |
| Open file | `Ctrl+O` |
| Save / Save As / Save All | `Ctrl+S` / `Ctrl+Shift+S` / `Ctrl+Alt+S` |
| Close tab | `Ctrl+W` |
| Zoom in / out / reset | `Ctrl++` / `Ctrl+-` / `Ctrl+0` |
| Undo / Redo, clipboard | standard (`Ctrl+Z`, `Ctrl+Y`, …) |
