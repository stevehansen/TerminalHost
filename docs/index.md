---
layout: default
title: TerminalHost
---

{% include nav.md %}

# TerminalHost

**TerminalHost** is a **cross-platform desktop application** that manages **terminal pairs per project directory**.
Each project tab contains a **custom command terminal** (Claude Code by default) and a **shell terminal**,
so you can switch between "AI assistant" and "regular shell" workflows without losing context.
A third **Run terminal** is available for dev server output.

| Platform | Executable | UI Framework | Default Shell |
|----------|------------|--------------|---------------|
| **Windows** | `host.exe` | WPF (.NET 8) | PowerShell |
| **macOS** | `host` | Avalonia (.NET 8) | zsh |

Both versions share the same core functionality, configuration format, and keyboard shortcuts.

## Key ideas

- **Directory-centric workflow**: one tab = one project folder, with paired terminals.
- **Fast switching**: toggle focus between custom/shell with `Ctrl+``.
- **Always-on split view**: custom + shell can stay visible side-by-side.
- **Runner terminal**: start/stop project run with `F5`, with URL detection for localhost servers.

## Quick links

- [Getting Started](getting-started)
- [Features](features) - Comprehensive feature reference
- [Feature Tour](feature-tour) - Workflow-based guide
- [Usage (CLI + configuration)](usage)
- [Keyboard Shortcuts](shortcuts)
- [Developer notes](developer)
