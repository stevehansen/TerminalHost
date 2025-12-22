---
layout: default
title: Feature Tour
---

{% include nav.md %}

# Feature Tour

This page walks through the primary workflows supported by **TerminalHost**: a WPF (.NET 8) desktop app (`host.exe`) that creates **one tab per project directory** with a **terminal pair** (custom AI terminal + PowerShell) and an optional **Run terminal** for dev-server output.

---

## Workflow 1: Per-project development (the default)

**Goal:** Keep your "AI assistant terminal" and your "real shell" side-by-side, scoped to a single project directory.

**How it works**
- Open a folder via CLI (`host .`, `host P:\MyProject`) or `Ctrl+N`.
- TerminalHost creates a tab for that folder.
- You get:
  - **Custom terminal** (Claude Code by default) for assisted work
  - **Shell terminal** (PowerShell) for direct commands
  - **Run terminal** for server output (optional/hidden until needed)

**Fast interactions**
- `Ctrl+`` toggles focus between Custom and Shell terminals.
- Use the layout toggles (Custom Full / Horizontal Split / Vertical Split) to match the task.

**Why this matters**
- You avoid "context loss": no separate windows, no "where was I?" terminal states, no switching between tools.

---

## Workflow 2: Task / focus mode

**Goal:** Stay on one task without UI distractions, while still retaining full shell capability.

**Recommended setup**
- Switch the layout to **Custom Full** when you're primarily working through the assistant terminal.
- Keep `Ctrl+`` as the "escape hatch" into PowerShell when you need to run commands manually.
- Use `Ctrl+T` to open the task panel for focus mode.

**Typical loop**
1. Describe a change in the Custom terminal.
2. Use `Ctrl+`` and run commands/tests in PowerShell.
3. Switch back and iterate.

---

## Workflow 3: Running a dev server and capturing output

**Goal:** Keep dev server logs available without stealing focus from the main terminal pair.

**How it works**
- Press `F5` to **Start/Stop** the run process.
- Press `Shift+F5` to **Force Stop** the run process.
- The **Run terminal** displays the server output.
- TerminalHost can detect and surface **localhost** links (useful when you start a local web server).

**Suggested habit**
- Leave the Run terminal visible while troubleshooting, hide it when stable.

---

## Workflow 4: PR review / change inspection

**Goal:** Review changes without leaving the project context.

**Panels**
- `Ctrl+G` opens the **Git changes** panel.
- `Ctrl+B` opens the **Git branch** switcher.
- `Ctrl+Shift+F` opens the **File explorer** panel.

This is designed to keep "inspect -> change -> run -> validate" inside a single tab.

---

## Power features

### Command palette
- `Ctrl+Shift+P` opens the command palette for quick actions.

### File operations
- `Ctrl+O` opens file viewer (preview mode, supports images).
- `Ctrl+Shift+E` opens file viewer (edit mode).
- `Ctrl+E` opens current folder in Explorer.

### Settings
- `Ctrl+,` opens the settings editor.
- `Ctrl+P` opens settings (Profiles section).
- Config file location: `%APPDATA%\TerminalHost\config.json`

### Scratch pad
- `Ctrl+Shift+N` opens the scratch pad for notes.

### Setup / dependency checker
- Run `host /setup` to launch the setup/dependency checker.
