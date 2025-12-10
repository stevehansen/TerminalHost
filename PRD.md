# Product Requirements Document: TerminalHost

## Overview

**TerminalHost** is a WPF desktop application that embeds multiple interactive terminal sessions using the Windows Terminal rendering backend. It provides a tabbed interface for managing CLI tools, each configured to run specific commands in designated working directories.

## Problem Statement

Developers frequently need to run multiple CLI tools simultaneously—package managers, build watchers, development servers, database shells—each requiring specific working directories and startup commands. Current solutions involve either:

- Multiple Windows Terminal windows/tabs requiring manual setup each session
- Custom scripts that spawn separate console windows
- IDE-integrated terminals that are tied to specific tooling

We need a lightweight, configurable application that launches predefined terminal sessions with a single action, consolidating them into one manageable interface.

## Goals

1. **Single-instance application** that manages all terminal sessions in one window
2. **Tabbed interface** for switching between active terminals
3. **Predefined configurations** specifying command + working directory combinations
4. **External invocation** to open new tabs from command line or other applications
5. **Full terminal emulation** supporting interactive CLIs (npm prompts, vim, SSH, etc.)

## Non-Goals (MVP)

- Split panes / docking layouts
- Session persistence across application restarts
- Remote terminal connections (SSH handled by CLI tools themselves)
- Custom theming beyond basic configuration
- Plugin system

## Domain Model

```
┌─────────────────────────────────────────────────────────────┐
│                      TerminalHost                           │
│                                                             │
│  ┌─────────────────┐       ┌─────────────────────────────┐ │
│  │ ProfileRegistry │       │      SessionManager         │ │
│  │                 │       │                             │ │
│  │ - profiles[]    │──────▶│ - activeSessions[]          │ │
│  │                 │       │ - createSession(profile)    │ │
│  └─────────────────┘       │ - closeSession(id)          │ │
│          │                 └─────────────────────────────┘ │
│          │                              │                   │
│          ▼                              ▼                   │
│  ┌─────────────────┐       ┌─────────────────────────────┐ │
│  │    Profile      │       │      TerminalSession        │ │
│  │                 │       │                             │ │
│  │ - id            │       │ - id                        │ │
│  │ - name          │       │ - profile                   │ │
│  │ - command       │       │ - terminalControl           │ │
│  │ - workingDir    │       │ - state (Running|Exited)    │ │
│  │ - icon?         │       │                             │ │
│  │ - shortcut?     │       │ - sendInput(text)           │ │
│  └─────────────────┘       │ - terminate()               │ │
│                            └─────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### Profile

A saved configuration template for launching a terminal session.

| Property     | Type     | Description                                      |
|--------------|----------|--------------------------------------------------|
| Id           | string   | Unique identifier (e.g., "dev-server")           |
| Name         | string   | Display name for tab/menu (e.g., "Dev Server")   |
| Command      | string   | Executable + arguments (e.g., "npm run dev")     |
| WorkingDir   | string   | Absolute path for working directory              |
| Icon         | string?  | Optional icon path or emoji for tab              |
| Shortcut     | string?  | Optional keyboard shortcut (e.g., "Ctrl+Shift+1")|

### TerminalSession

A running instance of a terminal bound to a specific profile.

| Property        | Type              | Description                           |
|-----------------|-------------------|---------------------------------------|
| Id              | Guid              | Unique session identifier             |
| Profile         | Profile           | Configuration used to spawn session   |
| State           | SessionState      | Running, Exited                       |
| ExitCode        | int?              | Process exit code when State=Exited   |
| TerminalControl | EasyTerminalControl | The WPF control instance            |

## User Stories

### US-1: Launch application with default tabs

> As a developer, I want TerminalHost to open my commonly-used terminals automatically so I don't configure them every session.

**Acceptance Criteria:**
- Application reads profiles from configuration file on startup
- Profiles marked as `autoStart: true` spawn immediately
- Each auto-started profile appears as a tab

### US-2: Open new tab from profile

> As a user, I want to open a new terminal tab from a list of saved profiles.

**Acceptance Criteria:**
- Menu/button shows available profiles
- Selecting a profile creates a new tab with that configuration
- Tab title shows profile name
- Terminal starts in specified working directory with specified command

### US-3: Open tab via command line

> As a user, I want to open a new tab in the running instance from the command line so I can integrate with scripts and other tools.

**Acceptance Criteria:**
- If TerminalHost is already running, command activates existing instance
- `TerminalHost.exe --profile "dev-server"` opens tab with named profile
- `TerminalHost.exe --command "npm start" --workdir "C:\Projects\App"` opens ad-hoc tab
- Focus switches to the new tab

### US-4: Close tab

> As a user, I want to close terminal tabs I no longer need.

**Acceptance Criteria:**
- Each tab has a close button
- Closing tab terminates the underlying process
- If process is still running, prompt for confirmation
- Closing last tab does not close the application

### US-5: Switch between tabs

> As a user, I want to quickly switch between terminal sessions.

**Acceptance Criteria:**
- Click tab to switch
- Ctrl+Tab cycles through tabs
- Ctrl+1-9 jumps to specific tab position
- Active tab is visually distinct

### US-6: Interact with terminal

> As a user, I want full terminal functionality including colors, cursor movement, and interactive prompts.

**Acceptance Criteria:**
- ANSI colors render correctly
- Interactive CLI apps work (npm prompts, vim, etc.)
- Copy/paste works (Ctrl+C/Ctrl+V or right-click)
- Scrollback buffer available

### US-7: Manage profiles

> As a user, I want to add, edit, and remove terminal profiles.

**Acceptance Criteria:**
- UI to view existing profiles
- Add new profile with name, command, working directory
- Edit existing profile
- Delete profile (with confirmation)
- Changes persist to configuration file

## Technical Approach

### Technology Stack

- **Framework**: WPF (.NET 8)
- **Terminal Control**: EasyWindowsTerminalControl (NuGet)
- **Configuration**: JSON file in `%APPDATA%\TerminalHost\`
- **Single Instance**: Named pipe for IPC

### Single Instance Implementation

```
┌──────────────────┐         ┌──────────────────────────────┐
│  New Process     │         │   Running Instance           │
│                  │         │                              │
│  1. Check mutex  │────────▶│  NamedPipeServer listening   │
│  2. Mutex exists │         │                              │
│  3. Connect pipe │────────▶│  Receives: --profile X       │
│  4. Send args    │         │  Creates new tab             │
│  5. Exit         │         │  Brings window to front      │
└──────────────────┘         └──────────────────────────────┘
```

### Configuration File Structure

```json
{
  "profiles": [
    {
      "id": "powershell",
      "name": "PowerShell",
      "command": "pwsh.exe",
      "workingDir": "%USERPROFILE%",
      "icon": "🔷",
      "autoStart": true
    },
    {
      "id": "dev-server",
      "name": "Dev Server",
      "command": "npm run dev",
      "workingDir": "C:\\Projects\\MyApp",
      "icon": "🚀",
      "shortcut": "Ctrl+Shift+D"
    }
  ],
  "settings": {
    "confirmOnClose": true,
    "showInSystemTray": false
  }
}
```

### Project Structure

```
TerminalHost/
├── TerminalHost.sln
├── src/
│   └── TerminalHost/
│       ├── App.xaml(.cs)
│       ├── MainWindow.xaml(.cs)
│       ├── Domain/
│       │   ├── Profile.cs
│       │   └── TerminalSession.cs
│       ├── Services/
│       │   ├── ProfileRegistry.cs
│       │   ├── SessionManager.cs
│       │   ├── ConfigurationService.cs
│       │   └── SingleInstanceService.cs
│       ├── Views/
│       │   ├── TerminalTabControl.xaml(.cs)
│       │   └── ProfileEditorDialog.xaml(.cs)
│       └── ViewModels/
│           ├── MainViewModel.cs
│           ├── TerminalTabViewModel.cs
│           └── ProfileEditorViewModel.cs
└── README.md
```

## MVP Scope

### Phase 1: Core Terminal Hosting

- [ ] WPF application shell with tab control
- [ ] EasyWindowsTerminalControl integration
- [ ] Open new tab with hardcoded PowerShell
- [ ] Close tab functionality
- [ ] Basic tab switching

### Phase 2: Profile System

- [ ] JSON configuration loading
- [ ] Profile domain model
- [ ] Open tab from profile
- [ ] Profile selection menu/dropdown

### Phase 3: Single Instance & CLI

- [ ] Mutex-based single instance detection
- [ ] Named pipe IPC server
- [ ] Command-line argument parsing
- [ ] External tab creation via CLI

### Phase 4: Profile Management

- [ ] Profile editor dialog
- [ ] Add/edit/delete profiles
- [ ] Configuration persistence

## Future Considerations

Items explicitly deferred from MVP:

- **Split panes**: Divide tab into multiple terminal regions
- **Drag-and-drop tabs**: Reorder or tear out tabs
- **Session restore**: Reopen previous session's tabs on startup
- **Theming**: Custom colors, fonts, opacity
- **SSH integration**: Built-in SSH connection profiles
- **Broadcast input**: Type in multiple tabs simultaneously

## Success Criteria

MVP is complete when:

1. User can launch application and see a terminal tab
2. User can open additional tabs from saved profiles
3. User can invoke `TerminalHost.exe --profile X` and see new tab appear in running instance
4. Interactive CLI applications (npm, git, etc.) work correctly
5. Configuration survives application restart

## Open Questions

1. **Tab overflow**: How to handle many tabs? Scrolling vs dropdown vs limit?
2. **Process exit behavior**: Auto-close tab when process exits, or show "[Process exited]"?
3. **Working directory resolution**: Support environment variables? Relative paths?
4. **Default shell**: If no profiles configured, what default? PowerShell Core > PowerShell > cmd?

---

*Document Version: 1.0*  
*Last Updated: 2025-01-XX*