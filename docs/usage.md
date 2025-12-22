---
layout: default
title: Usage
---

{% include nav.md %}

# Usage

## CLI usage

```text
host
host .
host P:\MyProject

host --workdir P:\MyProject
host -w P:\MyProject

host /setup
host --disable-single-instance   # Allow multiple instances (or -multi)
host --user-data-dir "C:\Path"   # Override configuration path (or -data)
```

## Layout modes

TerminalHost supports multiple layouts for the paired terminals:

* Custom Full
* Horizontal Split
* Vertical Split

Use the toolbar toggles to switch layout.

## Configuration

The configuration file lives at:

```text
%APPDATA%\TerminalHost\config.json
```

Edit it directly or via the settings editor (`Ctrl+,`).

Typical configuration areas:

* terminal commands (`customCommand`, `shellCommand`)
* window state, open folders
* quick commands + shortcuts
* link detection patterns
