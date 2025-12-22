---
layout: default
title: Getting Started
---

{% include nav.md %}

# Getting Started

## First Run

When you launch TerminalHost for the first time, the **Setup Window** will automatically appear to help you verify that all dependencies are installed (Claude Code, PowerShell, Git, etc.). After clicking "Continue", the main application will start and you won't see this setup again.

To skip this check, use `host --no-setup`.

## Open projects

### From the command line

```text
host              # Open/focus the application
host .            # Open project from current directory
host P:\MyProject # Open project from a specific path
host /setup       # Launch the setup/dependency checker
```

You can also use named arguments:

```text
host --workdir P:\MyProject
host -w P:\MyProject
```

### From within the app

* `Ctrl+N` — Open new project via folder picker

## The three terminals (per project tab)

* **Custom terminal**: AI assistant terminal (Claude Code by default), always visible
* **Shell terminal**: PowerShell for manual commands (`Ctrl+`` to switch focus)
* **Run terminal**: dev server output (`F5` to start/stop)
