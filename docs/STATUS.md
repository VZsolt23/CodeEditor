# Project Status and Remaining Work

_Last updated: 2026-07-04. Build verified: `dotnet build CodeEditor.slnx` → 0 warnings, 0 errors; app launch + graceful shutdown + settings round-trip smoke-tested; `WorkspaceService`, `SearchService`, `TextSearcher`, `OutputService`, `OutputLoggerProvider`, `TerminalService`, and `RoslynWorkspaceService` exercised with a headless test harness (77 checks: filtering, watcher debounce, CRUD, match options, truncation, cancellation, channel caps, shell lifecycle, and the full C# feature set — diagnostics, completion, quick info, signature help, definitions/references/rename, formatting — against a real MSBuild-loaded project)._

## Where We Are

**Phases 1–4 are complete.** The editor is a working daily driver with full C# language intelligence: tabbed editing, workspace explorer, Find in Files, in-editor find/replace, Output/Problems/Terminal panels, session restore, themes — plus Roslyn-powered live diagnostics with squiggles, completion, Quick Info, signature help, Go To Definition / Find References / Rename, Format Document, and semantic highlighting. TypeScript/JavaScript/HTML/CSS intelligence arrives with Phase 5 (LSP).

### Done — Phase 1: Application Shell

- [x] Solution layout: Core / Application / Infrastructure / Presentation (see [ARCHITECTURE.md](ARCHITECTURE.md))
- [x] MVVM via CommunityToolkit.Mvvm source generators
- [x] Dependency injection (`Microsoft.Extensions.DependencyInjection`, `ValidateOnBuild`)
- [x] Menu bar, toolbar, status bar (line/col, tab width, encoding label, language)
- [x] Dark + Light themes, runtime-switchable, persisted; extensible brush-key contract
- [x] Custom control templates (menus, tabs, buttons) so all chrome follows the theme
- [x] JSON settings at `%APPDATA%\CodeEditor\settings.json` (atomic writes, safe defaults on corrupt/missing file)
- [x] Serilog rolling file logging; global `DispatcherUnhandledException` handler
- [x] Collapsible sidebar and bottom panel (View menu toggles, splitters)

### Done — Phase 2: Editing

- [x] AvalonEdit integration, one shared editor instance across tabs, per-tab undo stacks
- [x] Multiple tabs: dirty indicator, close button, tooltip with full path, welcome screen when empty
- [x] Open / New / Save / Save As / Save All / Close (+`Ctrl+W`), save prompts on close and app exit
- [x] Recent files menu (persisted, de-duplicated, limit from settings, dead entries auto-removed)
- [x] Syntax highlighting by extension, with fallbacks (TS/JSX→JS, MSBuild→XML, Razor→HTML)
- [x] Line numbers, current-line highlight, C# auto-indentation, word wrap, zoom (`Ctrl+±`, `Ctrl+wheel`)
- [x] Optional timer-based auto-save (`autoSave` + `autoSaveIntervalSeconds` in settings)
- [x] Language registry in Core (extension → language id) feeding the status bar; ready for LSP routing

### Done — Phase 3: Workspace

- [x] **Open Folder** workspace: `IWorkspaceService` (Application) + `WorkspaceService` (Infrastructure, `FileSystemWatcher` with debounced per-directory change events); `ExplorerViewModel` + `FileTreeItemViewModel` lazy tree in the sidebar; `Ctrl+Shift+O`, File menu entries, empty-state Open Folder button; workspace name in the window title
- [x] Explorer interactions: open on double-click/Enter, context menu (new file/folder, rename, delete, refresh, copy path), auto-refresh on watcher events; open tabs are rebound when a file/folder is renamed (`DocumentsViewModel.HandlePathRenamed`)
- [x] File filtering: `explorerExcludedFolders` setting (defaults: `bin`, `obj`, `node_modules`, `.git`, `.vs`); watcher events inside excluded folders are suppressed
- [x] **Find in Files**: `ISearchService`/`SearchService` (literal text; match case + whole word; same ignore list; binary/oversized files skipped; 1000-match cap; cancellation) streamed into a SEARCH sidebar tab (`Ctrl+Shift+F`) with results grouped per file, match highlighting in excerpts, and click/Enter navigation to the match (selects it in the editor via `DocumentViewModel.PendingNavigation`)
- [x] **Find / Replace** in the editor: inline panel (`Ctrl+F` / `Ctrl+H`, `F3`/`Shift+F3`, Esc closes), match case + whole word, "N of M" status, seeds from the selection, all matches highlighted via a custom `IBackgroundRenderer` (current match stronger), Replace/Replace All (single undo step via `RunUpdate`); matching logic shared with Find in Files through `TextSearcher` (Core)
- [x] **Output panel**: `IOutputService`/`OutputService` with named, capped channels (Application); `OutputLoggerProvider` mirrors `ILogger` (Information+) into the "Log" channel alongside the Serilog file; panel UI with channel picker, Clear, autoscroll, batched appends
- [x] **Problems panel** infrastructure: `DiagnosticItem`/`DiagnosticSeverity` (Core), `ProblemsViewModel.SetDiagnostics(source, items)` replaces per source and sorts by severity/file/location; list UI with severity icons and double-click/Enter navigation to file/line — awaiting producers (Phases 4–5)
- [x] **Session restore**: `Session` block in settings (workspace, expanded folders, open tabs, active tab) captured in `MainWindow.OnClosing` (before close-all empties the tabs), restored best-effort after startup (missing paths skipped; recent-files order untouched); `restoreSession: false` opts out; verified end-to-end (seeded session → launch → graceful exit re-captures identical state)
- [x] **Terminal panel**: `ITerminalService`/`TerminalService` hosting the configured shell (`terminalShell` setting, default `cmd.exe`; forced to UTF-8 via `chcp 65001`) with redirected stdio and chunk-based output pumping (prompts without newlines render); lazily started on first input, Restart/Clear, capped transcript, `[Process exited]` notice, process tree killed on dispose so no shells outlive the app. Process-redirect approach per plan — good for builds/git/dotnet; full-screen TUI apps need the future ConPTY upgrade (backlog)

### Done — Phase 4: Roslyn (C#)

- [x] Host `Microsoft.CodeAnalysis` with `MSBuildWorkspace`: `ICodeAnalysisService`/`RoslynWorkspaceService` (Infrastructure) — `MSBuildLocator` registration, solution discovery (`.sln` → `.slnx` → recursive `.csproj` scan honoring the explorer excludes), best-effort load with per-project error tolerance. **Roslyn 4.14 cannot parse `.slnx`** ("No file format header found") — the fallback project scan handles it (verified against this repo: 4 projects load). Loads degrade gracefully: no SDK/solution → syntax-only diagnostics
- [x] Live diagnostics → Problems panel: `CSharpDiagnosticsCoordinator` (Presentation) debounces open-document edits (700 ms), analyzes unsaved text per document (semantic model diagnostics; edits accumulate in the forked solution so cross-file effects hold), pushes to `ProblemsViewModel` under source "csharp"; load status shown in the status bar
- [x] Squiggles + diagnostic hover: `DiagnosticItem` carries the span `Length`; the coordinator also publishes per-document diagnostics to `DocumentViewModel.Diagnostics`, which `DiagnosticSquiggleRenderer` (severity-colored zigzag underlines, offsets clamped against stale positions) draws in the shared editor; hovering a squiggle shows the message(s) in a themed tooltip
- [x] Completion: `ICodeAnalysisService.GetCompletionsAsync` (Roslyn `CompletionService` against unsaved text; requires `Microsoft.CodeAnalysis.CSharp.Features`) + `GetCompletionChangeAsync` (exact commit text via `GetChangeAsync` — display text isn't always insertion text); `EditorView` opens AvalonEdit's `CompletionWindow` on letters/`.`/`_`/Ctrl+Space, prefix filtering + preselection, themed; `DocumentViewModel` is the facade so the view stays service-free. No per-item description tooltips yet (needs lazy `GetDescriptionAsync`; deferred)

- [x] Go To Definition (`F12`), Find All References (`Shift+F12`), Rename Symbol (`F2`): `SymbolFinder`-based queries against unsaved text; single definition navigates directly, multiple definitions and references reuse the **search results tree** (built via the shared `SearchMatchFactory`, extracted from `SearchService`); rename (`Renamer`) returns whole-file `FileTextChange`s — applied to open buffers as one undoable edit (tab goes dirty) or written to disk for closed files; metadata-only symbols and invalid identifiers are rejected with a message. Commands live on `MainViewModel` (Edit menu + keybindings)

- [x] Quick Info hover: Roslyn `QuickInfoService` text merged into the editor hover tooltip alongside any diagnostics at the position (async with a hover token so stale results never pop)
- [x] Signature Help: semantic-model-based (Roslyn's own service is internal) — overload candidates from `GetMemberGroup`/constructors, active parameter from comma positions, active overload by argument count; shown in AvalonEdit's `OverloadInsightWindow` (Up/Down cycles) on `(`, `,`, and `Ctrl+Shift+Space`; `)` closes

- [x] Format Document (`Shift+Alt+F`): `Formatter.FormatAsync` on the unsaved buffer, minimal edits via `Document.GetTextChangesAsync` applied back-to-front as one undoable batch (caret preserved by AvalonEdit anchors); a snapshot check skips applying if the buffer changed during the request. Note: the Roslyn formatter fixes indentation/spacing on existing lines — it does not split single-line code (same as VS)

- [x] Semantic highlighting: `GetClassificationsAsync` (Roslyn `Classifier`, whole document, lexical noise filtered, sorted) → `DocumentViewModel.SemanticHighlights` → `SemanticHighlightColorizer` (a `DocumentColorizingTransformer` added after AvalonEdit's highlighting colorizer, so semantic colors win; binary-searched span lookup per line; 9 `Brush.Syntax.*` theme keys in both themes). The static `.xshd` colors remain underneath for un-classified spans and non-workspace files

**Phase 4 is complete.**

### Done — Phase 5 (in progress): LSP

- [x] Generic LSP client over stdio: `LspServerConnection` (Infrastructure, `StreamJsonRpc` + `SystemTextJsonFormatter`) — initialize handshake, full-text document sync (didOpen/didChange/didClose with version tracking), publishDiagnostics mapped to `DiagnosticItem` (0→1-based, code-prefixed messages), polite shutdown/exit. Stream-based, so the protocol layer is tested against an in-process fake server
- [x] TS/JS server lifecycle + diagnostics: `ILspService`/`LspService` spawns the `typeScriptServerCommand` setting (default `typescript-language-server --stdio`; tries the npm `.cmd` shim on Windows), lazily on first ts/js document, restarted per workspace, killed on dispose; missing server or dead process degrades gracefully with a status-bar hint (`npm install -g typescript-language-server typescript`). `LspDiagnosticsCoordinator` (Presentation) forwards open/change(debounced 500 ms)/close for `typescript`/`typescriptreact`/`javascript`/`javascriptreact` docs and routes pushed diagnostics into squiggles + Problems (source "typescript")

## Remaining Work

### Design overhaul — "Ember" (next up, takes priority over Phase 5 rest)

Full spec in [DESIGN.md](DESIGN.md): a unique warm-graphite + copper identity replacing the
VS Code-derived look. Phases (each: build → smoke test → **user visual review checkpoint**):

- [x] **A. Token foundation** — Ember Dark/Light dictionaries (58 identical keys each: all legacy `Brush.*` re-pointed + 14 new semantic tokens `Brush.Canvas`/`Surface.*`/`Border.*`/`Text.*`/`Accent.*`/`Hover`/`Selected`), theme ids unchanged (settings compat), display names "Ember Dark"/"Ember Light"; key-contract verified by script (all 44 referenced keys resolve in both). _Awaiting user visual review._
- [x] **B. Window chrome** — `WindowChrome` (40px caption, custom buttons, `GlassFrameThickness` keeps DWM shadow/rounding), unified top bar: ember app mark + menus (left), centered workspace pill (click = Open Folder, shows root name), themed min/max/close (close hovers red with `Brush.Text.OnColor`); toolbar row removed (commands live in menus/shortcuts; Menu is its own focus scope so routed Undo/Redo still reach the editor); maximize overhang compensated in `OnStateChanged`. _Awaiting user visual review._
- [x] **C. Workbench cards** — sidebar/editor/bottom panels are rounded cards (10px, `Brush.Surface`/`Surface.Editor`, subtle borders, content clipped via the `RoundedClip` attached behavior — plain `ClipToBounds` ignores `CornerRadius`) on the canvas with 8px gutters; splitters are the gutters (invisible; faint accent line on hover/drag); sidebar = "Files/Search" and bottom = Output/Problems/Terminal segmented pill switchers on `Surface.Sunken`; editor tabs are chips with ember underline + ember dirty dot; status footer is transparent with pill indicators; find panel restyled to `Surface.Overlay` 10px. _Awaiting user visual review._
- [x] **D. Controls pass** — implicit slim `ScrollBar` style app-wide (10px lane, 6px rounded thumb `Border.Strong`→`Text.Muted` hover, no arrows, transparent page-scroll track; horizontal variant via orientation trigger) — this restyles the editor, trees, lists, and terminal/output scrollbars and clears the dark-scrollbars backlog item; menus/context menus/combo popups rounded to 8px with 6px-rounded hover items; tooltips retemplated on `Surface.Overlay`; `PanelTextBoxStyle` retemplated (6px corners, sunken fill, ember caret, accent focus border); dialog/toolbar buttons rounded to 6px; `InputDialog` moved to `Surface.Overlay`. _Awaiting user visual review._
- [x] **E. Editor identity** — ember caret (re-resolved on tab switch; `Caret.CaretBrush` is not a DP), flat selection (no border; brush/current-line/find-match landed in A), editor padding 10,6; welcome screen redesigned (app mark, sentence-case copy, keycap chips for the four core shortcuts). The `.xshd` fallback palette was later recolored to Ember by `SyntaxHighlightingTheme` (see backlog), so first paint is on-theme; C# is further refined by semantic highlighting
- [x] **F. Motion & polish** — 120–180 ms opacity fades on splitter hover lines and the active-tab indicator (background hovers stay instant by choice — WPF brush animation would mean per-template overlay churn); stale Problems watermark copy fixed; **WCAG contrast audit scripted across 28 pairs, all passing** (primary ≥4.5, syntax ≥4, muted/comments ≥3; four hexes adjusted: dark comment `#7A6E60`, light muted `#91846F`, light type `#37806F`, light comment `#968871` — DESIGN.md synced)

**The Ember design overhaul is complete** (pending final user visual sign-off).

### Phase 5 — LSP (rest, resumes after the design overhaul)

- [ ] Map remaining LSP features: completion, hover, definition, rename, formatting → same editor plumbing Roslyn uses (consider a common `ILanguageService` facade routed by `LanguageInfo.Id`)
- [ ] HTML/CSS language servers (`vscode-html/css-languageservice`-based servers) reusing the same client
- [ ] Tag matching / auto-closing tags for HTML

### Phase 6 — Frameworks and Polish

- [ ] React (JSX/TSX via TS server), Angular Language Service integration
- [ ] JSON schema validation, XML validation, folding for JSON/XML
- [ ] Performance pass: startup time, large-file handling, virtualization in explorer/search results

### Cross-Cutting Backlog (items from [SPEC.md](SPEC.md) not yet tied to a phase)

- [ ] Bracket matching highlight (custom AvalonEdit renderer)
- [ ] Code folding (XML folding strategy exists in AvalonEdit; brace folding needs a strategy)
- [ ] Read-only mode for documents
- [ ] Settings hot-reload (watch `settings.json`; today changes apply on restart, except toggles changed via the View menu)
- [ ] Encoding handling: status bar hardcodes "UTF-8"; detect and display actual encoding, allow save-with-encoding
- [x] Dark AvalonEdit highlighting palette — `SyntaxHighlightingTheme` recolors every built-in `.xshd` definition to the Ember `Brush.Syntax.*` palette (by color name, idempotent) at startup before any editor renders and on theme change; fixes the light-colored flash before Roslyn's semantic pass on C# and permanently themes non-C# files. Scrollbars were themed in design-overhaul phase D
- [ ] Tab strip overflow (many tabs currently wrap; should scroll)
- [ ] Terminal ConPTY upgrade (real pseudoconsole + VT parsing so colors and full-screen TUI apps work; today's terminal is redirected-stdio plain text)
- [ ] Unit tests — none yet. Core/Application/Infrastructure are DI-friendly and UI-free by design; start with `LanguageRegistry`, `RecentFilesService`, `JsonSettingsService` (corrupt-file cases), then ViewModel tests with faked services
- [ ] CI (GitHub Actions: `dotnet build` + tests on `windows-latest`); no git repo initialized yet
- [ ] Minimap (explicitly deferred by the spec)

## Known Quirks (read before debugging)

1. **`.slnx`, not `.sln`** — `dotnet build CodeEditor.slnx`. Note: Roslyn 4.14's `MSBuildWorkspace` cannot open `.slnx`; `RoslynWorkspaceService` falls back to scanning `.csproj` files (bump the `Microsoft.CodeAnalysis.*` packages when `.slnx` support lands).
2. **NuGet audit pins** — `CodeEditor.Infrastructure.csproj` pins `Microsoft.Build.*`/`System.Security.Cryptography.Xml` (with `ExcludeAssets="runtime"`) purely to silence NU1903 on transitive Roslyn dependencies; runtime MSBuild comes from the installed SDK via `MSBuildLocator`, never from those packages.
3. **`System.Windows.Application` must be fully qualified** in the WPF project — the `CodeEditor.Application` namespace shadows it.
4. **Startup/exit block on async settings I/O** — anything on those paths needs `ConfigureAwait(false)` incl. `await using` disposals (a missed one caused a shutdown deadlock; fixed in `JsonSettingsService`, details in [ARCHITECTURE.md](ARCHITECTURE.md)).
5. **`EditorView` is reused across tabs** — never store per-document state in the view; it belongs on `DocumentViewModel`.
6. **Theme dictionaries must define the full brush-key set** — a missing key silently renders transparent/black.
