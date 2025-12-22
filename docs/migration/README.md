# TerminalHost macOS Migration Guide

This directory contains the complete migration plan for porting TerminalHost from WPF (Windows) to Avalonia (macOS).

## Overview

| Metric | Value |
|--------|-------|
| **Total Stages** | 8 |
| **Total Estimated Effort** | 37-55 developer days |
| **Target Platform** | macOS 12.0+ (Apple Silicon & Intel) |
| **UI Framework** | Avalonia UI 11.x |
| **Terminal Stack** | XtermSharp + Pty.Net |

## Stage Index

| Stage | Document | Focus | Effort | Risk |
|-------|----------|-------|--------|------|
| 1 | [STAGE-1-PROJECT-STRUCTURE.md](STAGE-1-PROJECT-STRUCTURE.md) | Build system & dependencies | 2-3 days | Low |
| 2 | [STAGE-2-SERVICE-ABSTRACTIONS.md](STAGE-2-SERVICE-ABSTRACTIONS.md) | Platform service interfaces | 3-5 days | Low |
| 3 | [STAGE-3-DOMAIN-MODELS.md](STAGE-3-DOMAIN-MODELS.md) | Remove P/Invoke from domain | 2-3 days | Medium |
| 4 | [STAGE-4-TERMINAL-CONTROL.md](STAGE-4-TERMINAL-CONTROL.md) | Terminal emulation stack | 5-7 days | **High** |
| 5 | [STAGE-5-CORE-UI.md](STAGE-5-CORE-UI.md) | App & MainWindow migration | 7-10 days | **High** |
| 6 | [STAGE-6-VIEWMODELS.md](STAGE-6-VIEWMODELS.md) | ViewModel platform independence | 3-5 days | Medium |
| 7 | [STAGE-7-VIEWS.md](STAGE-7-VIEWS.md) | All 44 XAML views | 10-15 days | **High** |
| 8 | [STAGE-8-TESTING.md](STAGE-8-TESTING.md) | Testing & polish | 5-7 days | Medium |

## Quick Start

1. Read the [main PRD](../PRD-MACOS-MIGRATION.md) for executive summary
2. Start with Stage 1 and proceed sequentially
3. Each stage has its own success criteria and verification steps
4. Complete each stage before moving to the next

## Key Technology Decisions

### Replacing Windows Dependencies

| Windows Component | macOS Replacement |
|-------------------|-------------------|
| WPF | Avalonia UI 11.x |
| EasyWindowsTerminalControl | XtermSharp + Pty.Net |
| ConPTY | POSIX PTY (via Pty.Net) |
| System.Windows.Forms.NotifyIcon | macOS Status Bar (native) |
| Microsoft.Win32.OpenFileDialog | Avalonia IStorageProvider |
| DispatcherTimer | ITimerService abstraction |
| user32.dll P/Invoke | Platform abstractions |

### New Service Abstractions

Created in Stage 2:
- `IFolderPickerService` - Folder dialogs
- `IFilePickerService` - File dialogs
- `IDispatcherService` - UI threading
- `ITimerService` - Periodic timers
- `IClipboardService` - Clipboard access
- `ISystemInfoService` - System information

## High-Risk Items

1. **Terminal Control (Stage 4)** - XtermSharp integration may need customization
2. **XAML Migration (Stage 7)** - 44 files with WPF-specific syntax
3. **Settings View** - Largest file (30K+ tokens) with complex bindings

## File Impact Summary

| Category | Count |
|----------|-------|
| Files to create | ~60 |
| Files to modify | ~30 |
| Files to delete | ~50 (XAML → AXAML replacement) |
| New service interfaces | 6 |
| ViewModels to update | 14 |

## Prerequisites

Before starting migration:

1. **Development Machine**: macOS 12.0+ with Xcode Command Line Tools
2. **.NET SDK**: .NET 8.0 SDK for macOS
3. **IDE**: JetBrains Rider or VS Code with C# extension
4. **Git**: For version control and submodules
5. **Testing**: Time for thorough manual testing

## Migration Order

```
Stage 1 (Build)
    ↓
Stage 2 (Services)
    ↓
Stage 3 (Domain) ──→ Stage 4 (Terminal)
    ↓                     ↓
Stage 5 (Core UI) ←──────┘
    ↓
Stage 6 (ViewModels)
    ↓
Stage 7 (Views)
    ↓
Stage 8 (Testing)
```

## Getting Help

- Each stage document has detailed implementation instructions
- Code examples are provided for all major changes
- Verification checklists help ensure completeness
- Known issues and mitigations are documented

## Rollback Strategy

Each stage can be rolled back independently:
1. Keep changes on a feature branch
2. Complete and verify each stage before merging
3. Tag releases at each stage completion

Good luck with the migration!
