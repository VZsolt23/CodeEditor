# Project Status and Remaining Work

_Last updated: 2026-07-06. Build verified: `dotnet build CodeEditor.slnx` → 0 warnings, 0 errors; app launch + graceful shutdown + settings round-trip smoke-tested. Headless test harness: **135 checks, all passing** — `WorkspaceService`/`SearchService`/`TextSearcher` (filtering, watcher debounce, CRUD, match options, truncation, cancellation, word boundaries), `OutputService`/`OutputLoggerProvider` (channel caps, log formatting), `RecentFilesService` (change event on the caller's thread), `TerminalService` (shell echo/exit/dispose-kill), `RoslynWorkspaceService` (the full C# feature set — diagnostics, completion, quick info, signature help, definitions/references/rename, formatting, classification — against a real MSBuild-loaded project), and the LSP client (`LspServerConnection`/`LspService`: handshake, document sync, diagnostics mapping, hover, completion, definition, and graceful degradation) against an in-process fake server._

## Where We Are

**Phases 1–4 are complete; the Ember design overhaul is complete; Phase 5 (LSP) is well underway.** The editor is a working daily driver with full C# language intelligence (Roslyn: live diagnostics with squiggles, completion, Quick Info, signature help, Go To Definition / Find References / Rename, Format Document, semantic highlighting) on top of tabbed editing, a workspace explorer with Devicon file icons, Find in Files, in-editor find/replace, Output/Problems/Terminal panels, and session restore — all wearing the unique warm-graphite "Ember" identity (see [DESIGN.md](DESIGN.md)). TypeScript/JavaScript already have LSP-backed diagnostics, hover, completion, and go-to-definition; references/rename/formatting for TS/JS and the HTML/CSS servers are the remaining Phase 5 work.

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
- [x] **Problems panel**: `DiagnosticItem`/`DiagnosticSeverity` (Core), `ProblemsViewModel.SetDiagnostics(source, items)` replaces per source and sorts by severity/file/location; list UI with severity icons and double-click/Enter navigation to file/line — now fed by both the C# ("csharp") and TS/JS ("typescript") diagnostics coordinators
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
- [x] Multi-server host pool: `LspServerHost` owns one server's process/connection/lifecycle (lazy start, per-workspace restart, kill on dispose, best-effort status); `LspService` is a thin router over a list of hosts built from `LspServerDescriptor`s (languages + settings command + display name + install hint), binding each opened document to a host by language and routing later notifications/requests by a file→host map
- [x] HTML + CSS/SCSS/Less servers: two more descriptors (`vscode-html-language-server` / `vscode-css-language-server` from `vscode-langservers-extracted`, via new `htmlServerCommand`/`cssServerCommand` settings). The `css` language was split into `css`/`scss`/`less` in `LanguageRegistry` so each gets its correct LSP languageId, and `LspLanguages` now covers html/css/scss/less — so all seven editor features (diagnostics, hover, completion, definition, references, rename, format) route to these servers too. `LspDiagnosticsCoordinator` now publishes diagnostics **grouped by each item's real source** (`typescript`/`html`/`css`) and clears sources that empty out — required because `ProblemsViewModel.SetDiagnostics` replaces per `DiagnosticItem.Source` (a single lumped source would leak html/css entries)
- [x] TS/JS server lifecycle + diagnostics: `ILspService`/`LspService` spawns the `typeScriptServerCommand` setting (default `typescript-language-server --stdio`; tries the npm `.cmd` shim on Windows), lazily on first ts/js document, restarted per workspace, killed on dispose; missing server or dead process degrades gracefully with a status-bar hint (`npm install -g typescript-language-server typescript`). `LspDiagnosticsCoordinator` (Presentation) forwards open/change(debounced 500 ms)/close for `typescript`/`typescriptreact`/`javascript`/`javascriptreact` docs and routes pushed diagnostics into squiggles + Problems (source "typescript")
- [x] TS/JS hover (Quick Info): `LspServerConnection.RequestHoverAsync` (`textDocument/hover`, extracts text from string/MarkupContent/MarkedString[] and strips markdown fences) → `ILspService.GetHoverAsync` (grabs the connection under the gate, runs the round-trip outside it so hover can't block doc sync; null when no server) → `DocumentViewModel.GetQuickInfoAsync` routes by language (Roslyn for C#, LSP for TS/JS, offset→0-based position). Reuses the existing editor hover tooltip unchanged. `LspLanguages` (Application) now single-sources the served language ids
- [x] TS/JS completion: `LspServerConnection.RequestCompletionAsync` (`textDocument/completion`, handles both the `CompletionItem[]` and `CompletionList` reply shapes; per-item insert text = textEdit.newText → insertText → label, snippet placeholders flattened; LSP kind → display tag; capped at 300) → `ILspService.GetCompletionsAsync` → `DocumentViewModel.GetCompletionsAsync` routes by language, flushes the buffer to the server (didChange) before requesting so completion sees the latest text, and computes the replaced-word span client-side. `CompletionItemInfo` gained an `InsertText` field; the shared completion entry (`RoslynCompletionData` → `LanguageCompletionData`) commits the item's own insert text (LSP) or resolves by index (Roslyn). No `completionItem/resolve` yet (no per-item docs / auto-import edits — follow-up)
- [x] TS/JS go to definition (`F12`) + find all references (`Shift+F12`): `LspServerConnection.RequestDefinitionAsync` (`textDocument/definition`, handles Location / Location[] / LocationLink[]) and `RequestReferencesAsync` (`textDocument/references` with `includeDeclaration`, sorted by file/line) both map to `SearchMatch`, reading target-file lines for excerpts (shared `AddLocation`) → `ILspService.GetDefinitionsAsync`/`GetReferencesAsync` → `DocumentViewModel.GetDefinitionsAsync`/`GetReferencesAsync` route by language (flush + 0-based position for LSP). Reuse the Roslyn result plumbing (single result navigates via `PendingNavigation`; multiple go to the search-results panel)
- [x] TS/JS rename (`F2`): `LspServerConnection.RequestRenameAsync` (`textDocument/rename`, parses the WorkspaceEdit `changes`-map and `documentChanges`-array shapes into per-file `LspFileEdits`; resource ops ignored) → `ILspService.RenameSymbolAsync`. `MainViewModel.ResolveRenameChangesAsync` branches: Roslyn returns whole-file changes directly; for LSP it flushes every open LSP doc (so the server matches the buffers), then rebuilds each affected file's new content by applying the range edits (`Core.Documents.TextEditApplier`) to the file's current text — the open buffer if open, else disk — producing the same `FileTextChange` list the existing apply loop consumes (open buffers = one undoable edit; closed files written to disk)
- [x] TS/JS format (`Shift+Alt+F`): `LspServerConnection.RequestFormattingAsync` (`textDocument/formatting`, options `tabSize`/`insertSpaces`; shared `ParseTextEdits` helper) → `ILspService.FormatDocumentAsync` → `DocumentViewModel.GetFormattingEditsAsync` routes by language and converts the LSP range edits to offset-based `TextEditInfo` (via the document), so `MainViewModel.FormatDocumentAsync` applies them through the same minimal-edit `RunUpdate` path Roslyn uses (caret preserved; snapshot guard). **All TS/JS LSP language features are now wired**; definition/references/rename/format share the `CanUseSymbolServices` gate (C# or LSP), replacing the old C#-only `CanUseLanguageServices`

### Done — Design overhaul: "Ember"

Full spec in [DESIGN.md](DESIGN.md): a unique warm-graphite + copper identity replacing the
VS Code-derived look. All six phases landed:

- [x] **A. Token foundation** — Ember Dark/Light dictionaries (58 identical keys each: all legacy `Brush.*` re-pointed + 14 new semantic tokens `Brush.Canvas`/`Surface.*`/`Border.*`/`Text.*`/`Accent.*`/`Hover`/`Selected`), theme ids unchanged (settings compat), display names "Ember Dark"/"Ember Light"; key-contract verified by script (all 44 referenced keys resolve in both).
- [x] **B. Window chrome** — `WindowChrome` (40px caption, custom buttons, `GlassFrameThickness` keeps DWM shadow/rounding), unified top bar: ember app mark + menus (left), centered workspace pill (click = Open Folder, shows root name), themed min/max/close (close hovers red with `Brush.Text.OnColor`); toolbar row removed (commands live in menus/shortcuts; Menu is its own focus scope so routed Undo/Redo still reach the editor); maximize overhang compensated in `OnStateChanged`.
- [x] **C. Workbench cards** — sidebar/editor/bottom panels are rounded cards (10px, `Brush.Surface`/`Surface.Editor`, subtle borders, content clipped via the `RoundedClip` attached behavior — plain `ClipToBounds` ignores `CornerRadius`) on the canvas with 8px gutters; splitters are the gutters (invisible; faint accent line on hover/drag); sidebar = "Files/Search" and bottom = Output/Problems/Terminal segmented pill switchers on `Surface.Sunken`; editor tabs are chips with ember underline + ember dirty dot; status footer is transparent with pill indicators; find panel restyled to `Surface.Overlay` 10px.
- [x] **D. Controls pass** — implicit slim `ScrollBar` style app-wide (10px lane, 6px rounded thumb `Border.Strong`→`Text.Muted` hover, no arrows, transparent page-scroll track; horizontal variant via orientation trigger) — this restyles the editor, trees, lists, and terminal/output scrollbars and clears the dark-scrollbars backlog item; menus/context menus/combo popups rounded to 8px with 6px-rounded hover items; tooltips retemplated on `Surface.Overlay`; `PanelTextBoxStyle` retemplated (6px corners, sunken fill, ember caret, accent focus border); dialog/toolbar buttons rounded to 6px; `InputDialog` moved to `Surface.Overlay`.
- [x] **E. Editor identity** — ember caret (re-resolved on tab switch; `Caret.CaretBrush` is not a DP), flat selection (no border; brush/current-line/find-match landed in A), editor padding 10,6; welcome screen redesigned (app mark, sentence-case copy, keycap chips for the four core shortcuts). The `.xshd` fallback palette was later recolored to Ember by `SyntaxHighlightingTheme` (see backlog), so first paint is on-theme; C# is further refined by semantic highlighting
- [x] **F. Motion & polish** — 120–180 ms opacity fades on splitter hover lines and the active-tab indicator (background hovers stay instant by choice — WPF brush animation would mean per-template overlay churn); stale Problems watermark copy fixed; **WCAG contrast audit scripted across 28 pairs, all passing** (primary ≥4.5, syntax ≥4, muted/comments ≥3; four hexes adjusted: dark comment `#7A6E60`, light muted `#91846F`, light type `#37806F`, light comment `#968871` — DESIGN.md synced)

### Done — File icons (Devicon)

- [x] Explorer file nodes show language-specific icons from the bundled Devicon font (`Assets/Fonts/devicon.ttf`, family "devicon", MIT — see `DEVICON-LICENSE.txt`; added as a WPF `Resource`). `FileIconCatalog` (Services) maps ~40 extensions + special names (Dockerfile, package.json, .gitignore) to a glyph codepoint and brand color; `FileTreeItemViewModel` resolves it at construction. The tree template shows the Devicon glyph (brand-colored) for known files and falls back to the generic themed Segoe glyph for folders and unknown files. Font family + all mapped codepoints verified against the TTF via `GlyphTypeface`.

## Remaining Work

### Phase 5 — LSP (rest)

- [ ] Tag matching / auto-closing tags for HTML (editor-side; the HTML server also offers `linkedEditingRange`/`onTypeFormatting` that could drive it)
- [ ] Consider a common `ILanguageService` facade routed by `LanguageInfo.Id` to collapse the C#-vs-LSP branching now repeated across `DocumentViewModel`/`MainViewModel`

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
- [ ] Unit tests — none in-repo yet (the 112-check harness that verifies each change lives in an external scratchpad, not committed). Core/Application/Infrastructure are DI-friendly and UI-free by design; start by porting the harness into a real test project — `LanguageRegistry`, `RecentFilesService`, `JsonSettingsService` (corrupt-file cases), the services already covered by the harness — then ViewModel tests with faked services
- [ ] CI (GitHub Actions: `dotnet build` + tests on `windows-latest`). A local git repo exists (commits made one-per-change, local identity `VZsolt23`); no remote yet
- [ ] Minimap (explicitly deferred by the spec)

## Known Quirks (read before debugging)

1. **`.slnx`, not `.sln`** — `dotnet build CodeEditor.slnx`. Note: Roslyn 4.14's `MSBuildWorkspace` cannot open `.slnx`; `RoslynWorkspaceService` falls back to scanning `.csproj` files (bump the `Microsoft.CodeAnalysis.*` packages when `.slnx` support lands).
2. **NuGet audit pins** — `CodeEditor.Infrastructure.csproj` pins `Microsoft.Build.*`/`System.Security.Cryptography.Xml` (with `ExcludeAssets="runtime"`) purely to silence NU1903 on transitive Roslyn dependencies; runtime MSBuild comes from the installed SDK via `MSBuildLocator`, never from those packages.
3. **`System.Windows.Application` must be fully qualified** in the WPF project — the `CodeEditor.Application` namespace shadows it.
4. **Startup/exit block on async settings I/O** — anything on those paths needs `ConfigureAwait(false)` incl. `await using` disposals (a missed one caused a shutdown deadlock; fixed in `JsonSettingsService`, details in [ARCHITECTURE.md](ARCHITECTURE.md)).
5. **`EditorView` is reused across tabs** — never store per-document state in the view; it belongs on `DocumentViewModel`.
6. **Theme dictionaries must define the full brush-key set** — a missing key silently renders transparent/black.
