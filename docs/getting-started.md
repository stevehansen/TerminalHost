---
layout: default
title: Getting Started
---

{% include nav.md %}

# Getting Started

TerminalHost is available for Windows and macOS:

| Platform | Executable | UI Framework | Default Shell |
|----------|------------|--------------|---------------|
| **Windows** | `host.exe` | WPF (.NET 8) | PowerShell |
| **macOS** | `host` | Avalonia (.NET 8) | zsh |

## First Run

When you launch TerminalHost for the first time, the **Setup Window** will automatically appear to help you verify that all dependencies are installed (Claude Code, your shell, Git, etc.). After clicking "Continue", the main application will start and you won't see this setup again.

To skip this check, use `host --no-setup`.

## Open projects

### From the command line

**Windows:**
```text
host              # Open/focus the application
host .            # Open project from current directory
host P:\MyProject # Open project from a specific path
host /setup       # Launch the setup/dependency checker
```

**macOS:**
```text
host              # Open/focus the application
host .            # Open project from current directory
host ~/Projects   # Open project from a specific path
host /setup       # Launch the setup/dependency checker
```

You can also use named arguments:

```text
host --workdir /path/to/project
host -w /path/to/project
```

### From within the app

* `Ctrl+N` (Windows) / `Cmd+N` (macOS) — Open new project via folder picker

## The three terminals (per project tab)

* **Custom terminal**: AI assistant terminal (Claude Code by default), always visible
* **Shell terminal**: PowerShell (Windows) or zsh (macOS) for manual commands (`Ctrl+`` to switch focus)
* **Run terminal**: dev server output (`F5` to start/stop)

## Configuration

Settings are stored in a JSON file:

| Platform | Location |
|----------|----------|
| **Windows** | `%APPDATA%\TerminalHost\config.json` |
| **macOS** | `~/.config/TerminalHost/config.json` |

Open settings with `Ctrl+,` (Windows) or `Cmd+,` (macOS).
