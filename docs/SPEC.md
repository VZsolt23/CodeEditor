# Original Project Specification

_This is the original prompt/brief the project was started from, preserved verbatim for reference. Current progress against it is tracked in [STATUS.md](STATUS.md)._

---

# Project: Modern Windows Code Editor

## Goal

Build a modern, lightweight code editor for Windows inspired by Visual Studio Code and JetBrains Rider, but significantly simpler. The editor should focus on excellent support for modern .NET and web development while maintaining a clean, fast, and modular architecture.

This is **not** intended to be a Visual Studio or Rider clone. Instead, it should be a well-designed editor that can evolve over time without requiring major architectural changes.

---

# Primary Language Support

The editor should be optimized primarily for the following languages and technologies:

## Core Languages

- C#
- HTML
- CSS
- JavaScript
- TypeScript

## Frontend Frameworks

- React
- Angular

## Configuration & Data Files

- JSON
- XML
- YAML
- Markdown

## Common .NET Files

- .cs
- .csproj
- .sln
- .props
- .targets
- .config
- appsettings.json
- launchSettings.json
- Directory.Build.props
- Directory.Build.targets

The architecture should allow adding additional language support later.

---

# Technology Stack

- C#
- .NET 9+
- WPF
- MVVM
- Dependency Injection
- AvalonEdit
- Roslyn
- Language Server Protocol (LSP)

---

# Language Services

## C#

Use Roslyn to provide:

- Syntax highlighting
- IntelliSense
- Auto-completion
- Diagnostics
- Code navigation
- Go To Definition
- Find References
- Rename Symbol
- Hover information
- Signature Help
- Code formatting

---

## TypeScript / JavaScript

Use the TypeScript Language Server through LSP.

Support:

- IntelliSense
- Diagnostics
- Auto-completion
- Go To Definition
- Rename Symbol
- Formatting

---

## HTML

Support:

- Syntax highlighting
- Auto-completion
- Tag matching
- Auto-closing tags
- Formatting

---

## CSS

Support:

- Syntax highlighting
- Auto-completion
- Diagnostics
- Formatting

---

## React

Support:

- JSX
- TSX
- Component navigation
- Auto-completion
- Formatting

---

## Angular

Support:

- TypeScript
- HTML templates
- Angular Language Service
- Auto-completion
- Diagnostics

---

## JSON

Support:

- Validation
- Formatting
- Folding
- Auto-completion where available

---

## XML

Support:

- Validation
- Formatting
- Folding
- Auto-completion
- Matching tags

---

# Design Goals

- Native Windows application
- Fast startup
- Responsive UI
- Modular architecture
- Clean separation of concerns
- Easy to maintain
- Extensible language support

---

# Main Window

Include:

- Menu bar
- Toolbar
- Status bar
- Sidebar
- Bottom panel
- Tabbed document interface

---

# File Explorer

Support:

- Open Folder
- Workspace tree
- File filtering
- Recent files

---

# Editor Features

Use AvalonEdit.

Features:

- Multiple tabs
- Syntax highlighting
- Line numbers
- Current line highlight
- Bracket matching
- Auto indentation
- Undo / Redo
- Find
- Replace
- Find in Files
- Zoom
- Word wrap
- Code folding
- Read-only mode
- Minimap (future enhancement)

---

# Workspace

Open a folder as a workspace.

Display:

- Folder tree
- Open documents
- Recent files

---

# Bottom Panel

Include:

- Output
- Problems
- Terminal

---

# Settings

Store settings as JSON.

Settings should include:

- Theme
- Font family
- Font size
- Tab width
- Word wrap
- Auto save
- Recent files limit

---

# Themes

Initially provide:

- Light
- Dark

The theme system should be extensible.

---

# Suggested Architecture

```
Presentation
│
├── Views
├── ViewModels
└── Commands

Application
│
├── Services
├── Interfaces
└── Models

Infrastructure
│
├── File System
├── Roslyn
├── LSP
├── Settings
└── Logging

Core
│
├── Documents
├── Workspace
├── Editor
└── Utilities
```

---

# Development Roadmap

## Phase 1

- Project setup
- WPF shell
- MVVM
- Dependency Injection
- Navigation
- Menu
- Status bar

---

## Phase 2

- AvalonEdit integration
- Multiple tabs
- File open/save
- Recent files

---

## Phase 3

- Workspace explorer
- Folder loading
- Search
- Bottom panels

---

## Phase 4

- Roslyn integration
- Full C# language services

---

## Phase 5

- Language Server Protocol integration
- TypeScript
- JavaScript
- HTML
- CSS

---

## Phase 6

- React support
- Angular support
- JSON/XML improvements
- Performance optimization

---

# Code Style Requirements

- Follow SOLID principles.
- Use async/await where appropriate.
- Keep components loosely coupled.
- Prefer dependency injection.
- Avoid large ViewModels.
- Keep methods focused and small.
- Use meaningful naming.
- Produce production-quality code.
- Write XML documentation for public APIs where appropriate.
- Ensure every implementation compiles before proceeding.

---

# Expected Output

Build the editor incrementally.

For every step:

1. Explain the architecture and design decisions.
2. Generate complete production-ready code.
3. Keep the solution modular.
4. Avoid placeholder implementations whenever possible.
5. Verify that the solution builds successfully before moving to the next feature.
6. Prioritize maintainability, readability, and long-term scalability over shortcuts.
