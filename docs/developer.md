---
layout: default
title: Developer
---

{% include nav.md %}

# Developer

## Technology stack

- **WPF** on **.NET 8**
- Terminal control: **EasyWindowsTerminalControl**
- MVVM: **CommunityToolkit.Mvvm**
- Single instance: **Mutex + named pipe IPC**

## Build commands

```bash
dotnet build
dotnet run --project src/TerminalHost/TerminalHost
dotnet publish src/TerminalHost/TerminalHost -c Release -o publish
```
