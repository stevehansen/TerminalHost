# Multiple AI Assistant Support

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
    }
  ]
}
```

### Per-Project Configuration

```json
{
  "directorySettings": {
    "p:\\myproject": {
      "activeAiAssistantId": "claude"
    }
  }
}
```

## Domain Model

- `AiAssistant`: Represents an AI tool with its command, icon, and status.
- `IAiAssistantService`: Manages configured assistants and project-level selections.

```