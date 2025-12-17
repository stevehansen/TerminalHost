# PRD: Multiple AI Assistant Support

This document details the implementation of multiple AI CLI assistant support in TerminalHost.

## Overview

TerminalHost supports multiple AI CLI tools (Claude Code, Gemini CLI, OpenAI Codex, GitHub Copilot) with per-project selection and full customization.

## Features

### Built-in AI Assistants

| Assistant | Command | Icon | Detection | Default State |
|-----------|---------|------|-----------|---------------|
| Claude Code | `%USERPROFILE%\.local\bin\claude.exe` | `Claude` | `claude --version` | Enabled (default) |
| Gemini CLI | `gemini` | `Gemini` | `gemini --version` | Disabled |
| OpenAI Codex | `codex` | `Codex` | `codex --version` | Disabled |
| GitHub Copilot | `gh copilot` | `Copilot` | `gh copilot --version` | Disabled |

### Per-Project AI Selection

- Toolbar dropdown in terminal pair view to select active AI
- Selection persists per project directory in `directorySettings`
- Switching AI immediately restarts the custom terminal

### Settings Management

- Dedicated "AI Assistants" section in Settings
- Add, edit, remove, and reorder AI assistants
- Toggle enabled/disabled state
- Set default assistant

### Setup Detection

- Setup window auto-detects installed AI CLIs
- Offers to enable detected assistants
- Validates at least one assistant is enabled

## Configuration Schema

### Global Configuration (config.json)

```json
{
  "aiAssistants": [
    {
      "id": "claude",
      "name": "Claude Code",
      "command": "%USERPROFILE%\\.local\\bin\\claude.exe",
      "icon": "Claude",
      "detectionCommand": "claude --version",
      "enabled": true,
      "isDefault": true
    },
    {
      "id": "gemini",
      "name": "Gemini CLI",
      "command": "gemini",
      "icon": "Gemini",
      "detectionCommand": "gemini --version",
      "enabled": false,
      "isDefault": false
    }
  ]
}
```

### Per-Project Configuration

```json
{
  "directorySettings": {
    "p:\\myproject": {
      "activeAiAssistantId": "claude",
      // ... other settings
    }
  }
}
```

## Implementation

### Domain Model

**AiAssistant.cs**
```csharp
public class AiAssistant
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Command { get; set; }
    public string Icon { get; set; }
    public string? DetectionCommand { get; set; }
    public bool Enabled { get; set; }
    public bool IsDefault { get; set; }
}
```

### Service Layer

**IAiAssistantService**
- `GetAllAssistants()` - All configured assistants
- `GetEnabledAssistants()` - Only enabled assistants
- `GetDefaultAssistant()` - The default assistant
- `GetAssistantForDirectory(path)` - Active assistant for a project
- `SetAssistantForDirectory(path, id)` - Set project's active assistant
- `SaveAssistant(assistant)` - Add/update assistant
- `DeleteAssistant(id)` - Remove assistant

### Migration

On config load, if `aiAssistants` is empty:
1. Initialize with default assistants
2. Migrate legacy `CustomCommand`/`CustomCommandName`/`CustomCommandIcon` to claude assistant if different from defaults

## UI Components

### Toolbar AI Selector

Location: Terminal pair toolbar (after layout buttons)

```
[Custom Full] [H-Split] [V-Split] | [Claude Code v] | [Links] [Explorer]
```

Dropdown shows:
- Icon + Name for each enabled assistant
- Current selection highlighted

### Settings > AI Assistants

List view with:
- Drag handles for reordering
- Enable/disable checkbox
- Default indicator
- Edit/Delete buttons

Edit form:
- Name (text)
- Command (text with file picker)
- Icon (emoji/text picker)
- Detection Command (optional)

## Files Modified

| File | Changes |
|------|---------|
| `Domain/AiAssistant.cs` | New - AI assistant model |
| `Domain/AppConfiguration.cs` | Add `AiAssistants` list, `DirectorySettings.ActiveAiAssistantId` |
| `Domain/TerminalPair.cs` | Add `ReplaceCustomTerminal()` method |
| `Services/AiAssistantService.cs` | New - AI management service |
| `Services/ConfigurationService.cs` | Migration logic |
| `ViewModels/MainViewModel.cs` | Use IAiAssistantService, handle AI switching |
| `ViewModels/TerminalPairTabViewModel.cs` | AI selector properties |
| `ViewModels/SettingsTabViewModel.cs` | AI Assistants section |
| `ViewModels/SetupViewModel.cs` | Detect AI CLIs |
| `Views/Tabs/TerminalPairView.xaml` | AI dropdown in toolbar |
| `Views/SettingsView.xaml` | AI Assistants UI |
| `App.xaml.cs` | Register IAiAssistantService |

---

*Document Version: 1.0*
*Created: 2025-12-17*
