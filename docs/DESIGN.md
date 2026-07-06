# Ember — Design Language

_The visual identity for Code Editor. Replaces the VS Code-derived look. This document is the
source of truth for colors, layout, and the redesign plan; STATUS.md tracks progress against it._

## Why a new design

The current UI is a competent VS Code clone: `#1E1E1E` surfaces, `#007ACC` blue, a colored status
bar, uppercase panel headers, an edge-to-edge grid of hard 1px splits, and the VS dark+ syntax
palette. Every one of those choices reads as "VS Code". The goal is an identity that is
recognizable at a glance as *this* editor, while meeting current design expectations: soft
layered surfaces, generous rhythm, one disciplined accent, quiet chrome, purposeful motion.

## Identity: "Ember"

Warm graphite surfaces with a single copper-ember accent. Calm and warm where VS Code is cool and
technical; soft and layered where Sublime is flat and dense. Two faces of the same identity:

- **Ember Dark** — warm near-black graphite, cream text, copper accent.
- **Ember Light ("Parchment")** — warm paper white, ink text, deeper copper accent.

### Principles

1. **Warm neutrals.** Every gray carries a touch of warmth (brown/amber undertone). No pure or
   blue-grays anywhere.
2. **One accent, used sparingly.** Copper marks *the* active thing: active tab indicator, focused
   input ring, caret, selected segment, primary button. It never decorates.
3. **Depth from layers, not lines.** Panels are rounded cards floating on a darker canvas with
   gutters between them. Hairline borders are subtle; splitters are invisible until hovered.
4. **Quiet chrome, content forward.** The editor text is the highest-contrast element on screen.
   Chrome text sits one or two contrast steps below. No colored status bar, no uppercase shouting.
5. **Rhythm.** 4px base unit. Panel gutters 8px, card padding 12px, control padding 8×5px.
   Corner radii: 10px cards, 6px controls, 999px pills.
6. **Motion as feedback.** 120–150ms ease-out fades on hover/selection; nothing bounces, nothing
   slides for decoration. (WPF: `Trigger.EnterActions` with short `DoubleAnimation`s, applied only
   where cheap.)

## Color tokens

Semantic token set. Theme dictionaries keep the established contract: **every key defined in both
themes, all consumption via `DynamicResource`.** Existing `Brush.*` keys are re-pointed or
replaced; new chrome uses the new keys. (Settings ids stay `Dark`/`Light`; only display names
become "Ember Dark" / "Ember Light".)

### Surfaces and chrome

| Token | Ember Dark | Ember Light | Use |
|---|---|---|---|
| `Canvas` | `#131110` | `#EDE7DC` | Window background behind the cards |
| `Surface` | `#1C1917` | `#F8F4EC` | Panel cards (explorer, bottom panel) |
| `Surface.Editor` | `#211D1A` | `#FFFCF5` | Editor card (brightest focus area) |
| `Surface.Overlay` | `#282320` | `#FFFFFF` | Menus, popups, completion, dialogs |
| `Surface.Sunken` | `#171412` | `#F0EBE1` | Terminal/output wells, input fields |
| `Border.Subtle` | `#2C2724` | `#DFD7C9` | Card outlines, dividers |
| `Border.Strong` | `#3D3630` | `#C9BFAE` | Hover outlines, popup borders |
| `Text.Primary` | `#EDE5DA` | `#33291F` | Code and primary content |
| `Text.Secondary` | `#B0A494` | `#6E6252` | Labels, inactive tabs |
| `Text.Muted` | `#776C5E` | `#91846F` | Hints, watermarks, line numbers |
| `Accent` | `#E1804A` | `#B65C2E` | The one accent (see principle 2) |
| `Accent.Hover` | `#EE9663` | `#C96F41` | Hovered accent elements |
| `Accent.Soft` | `#3A2A1F` | `#F3DFCE` | Accent-tinted fills (selected pill bg) |
| `Hover` | `#282320` | `#EFE8DB` | List/menu/tab hover fill |
| `Selected` | `#322B25` | `#E9DFCE` | Selected list rows (with 2px accent bar) |

### Editor

| Token | Ember Dark | Ember Light |
|---|---|---|
| Selection | `#463526` | `#F0DBC2` |
| Current line | `#26211D` | `#F6F0E4` |
| Caret | accent | accent |
| Find match | `#5A4022` (current: `#7A4E22`) | `#F5DEB9` (current: `#EDC98F`) |
| Error / Warning / Info | `#E5534B` / `#D9A13F` / `#5FA8A0` | `#C33C2E` / `#A5751B` / `#3C7F78` |

### Syntax (replaces VS dark+ palette)

| Classification | Ember Dark | Ember Light |
|---|---|---|
| Keyword | `#E1804A` (ember) | `#B65C2E` |
| Control keyword | `#D9707C` (rosewood) | `#B04455` |
| Type | `#7FBFAF` (patina — oxidized copper) | `#37806F` |
| Method | `#E3C388` (brass) | `#8F6B2A` |
| Parameter / local | `#C9B8A4` (clay) | `#5C4F3D` |
| String | `#A8B87C` (olive) | `#6D7F3E` |
| Number | `#D9A66F` (amber) | `#A06A2C` |
| Comment | `#7A6E60` (graphite) | `#968871` |
| Preprocessor | `#8A7E70` | `#8A7E70` |

The syntax story is metallurgy: keywords glow ember, types are patina, methods brass, literals
amber/olive. Cohesive, warm, and not any existing scheme.

## Layout and chrome

### Window

Borderless window (`WindowChrome`), Win11 rounded corners. One unified **top bar** replaces
title bar + menu + toolbar: app mark (ember square glyph), the menus (File/Edit/View/Help as
quiet text buttons), a **centered workspace pill** showing the open folder name (future home of
the command palette), then window controls drawn to match the theme. Toolbar icon buttons fold
into the top bar's right side (new/open/save become menu+shortcut only; Undo/Redo dropped from
chrome — they live on the keyboard).

### Workbench

Cards on canvas: sidebar card | editor card | bottom card, separated by 8px gutters over
`Canvas`. Splitters are the gutters themselves — invisible, cursor change + faint accent line on
hover. Sidebar and bottom panel headers use **segmented pill switchers** ("Files / Search",
"Output / Problems / Terminal") instead of tab strips — selected segment gets `Accent.Soft` fill
with `Accent` text.

### Editor tabs

Chips, not a strip: rounded (6px) tab chips sitting in a quiet row above the editor card content,
gap 4px. Active chip = `Surface.Editor` fill + 2px ember underline inside the chip; inactive =
transparent with `Text.Secondary`, hover = `Hover` fill. Dirty state = small ember dot replacing
the close glyph until hovered. No trapezoids, no full-height dividers.

### Status footer

Transparent (canvas shows through), 26px. Left: status text in `Text.Secondary`. Right: **pill
indicators** — language, `Ln x, Col y`, spaces, encoding — each a quiet rounded chip; the
language-services state pill turns `Accent.Soft` while loading. No colored bar.

### Empty states and details

- Welcome screen: app mark, short sentence-case copy, shortcuts rendered as keycap chips.
- Explorer/search/problems keep their trees, restyled: 24px rows, 6px-radius selection with a
  2px ember bar, folder glyphs in `Text.Muted`.
- Scrollbars: custom slim overlay style — 6px rounded thumb (`Border.Strong`, hover `Text.Muted`),
  no track, no arrows. Applies everywhere including the editor (clears an existing backlog item).
- Focus: 1.5px `Accent` ring outside inputs; keyboard-focus only where distinguishable.
- Typography: UI = Segoe UI Variable Text 13px (fallback Segoe UI); headers sentence case,
  12px semibold `Text.Secondary`. Code stays Cascadia Code.
- Find panel, input dialog, completion popup, insight window, tooltips: `Surface.Overlay`, 10px
  radius, `Border.Strong`, single soft shadow (popups only — the one permitted shadow).

## Implementation plan

Phased; each phase ends with build (0 warnings) + smoke test + a **user visual review checkpoint**
(screenshots can't be taken headlessly).

- **A. Token foundation** — new theme dictionaries (all keys, both themes), re-point existing
  `Brush.*` keys to Ember values, add new semantic keys, update `ThemeService` display names.
  App runs with new colors on old layout.
- **B. Window chrome** — `WindowChrome` borderless window, unified top bar with menus + workspace
  pill + themed window controls; remove old toolbar row.
- **C. Workbench cards** — canvas + card layout with gutters, hover-reveal splitters, segmented
  pill switchers for sidebar/bottom panels, editor tab chips, status footer pills.
- **D. Controls pass** — scrollbars (app-wide), menus/context menus, combo, buttons, inputs,
  dialogs, find panel, tooltips, completion/insight popups on `Surface.Overlay`.
- **E. Editor identity** — syntax palettes both themes, selection/current-line/caret colors,
  find-match brushes, gutter/line-number styling, welcome screen.
- **F. Motion & polish** — hover/selection fades, focus rings, empty-state copy pass, contrast
  audit (≥4.5:1 body, ≥3:1 secondary), light-theme parity check.

### Constraints carried over

- Theme contract: every brush key in both dictionaries; `DynamicResource` everywhere.
- No per-pixel bitmap effects on hot paths; `DropShadowEffect` only on popups.
- `EditorView` reuse, ViewModel layering, and 0-warning baseline are untouched — this is a
  presentation-layer effort (Themes/, Views/, control templates) plus `ThemeService` metadata.
