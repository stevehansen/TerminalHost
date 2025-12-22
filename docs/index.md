---
layout: default
title: TerminalHost
---

{% include nav.md %}

# TerminalHost

**TerminalHost** (`host.exe`) is a **WPF desktop application** that manages **terminal pairs per project directory**.
Each project tab contains a **custom command terminal** (Claude Code by default) and a **shell terminal** (PowerShell),
so you can switch between "AI assistant" and "regular shell" workflows without losing context.
A third **Run terminal** is available for dev server output.

## Key ideas

- **Directory-centric workflow**: one tab = one project folder, with paired terminals.
- **Fast switching**: toggle focus between custom/shell with `Ctrl+``.
- **Always-on split view**: custom + shell can stay visible side-by-side.
- **Runner terminal**: start/stop project run with `F5`, with URL detection for localhost servers.

## Quick links

- [Getting Started](getting-started)
- [Usage (CLI + configuration)](usage)
- [Keyboard Shortcuts](shortcuts)
- [Feature Tour](feature-tour)
- [Developer notes](developer)
