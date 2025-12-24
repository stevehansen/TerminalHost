# PRD: Search and Productivity Features

This document outlines planned search and productivity features for TerminalHost to enhance developer workflow efficiency.

## Current State

TerminalHost currently includes:
- **Search Across Files** (Ctrl+F3) - Full-text search with replace functionality
- File Explorer panel with file search by name
- Detected Links feature for scanning terminal output
- Scratch Pad for per-project notes
- Command Palette for quick actions
- Task/Focus Mode for work organization

## Goals

1. **Powerful search** - Find code and content across the entire project
2. **Terminal integration** - Search within terminal output
3. **Code reuse** - Save and quickly insert common snippets
4. **Session management** - Save and restore workspace states
5. **Tool integration** - Connect with external tools seamlessly

---

## Search Across Files (High Priority) - IMPLEMENTED

**Status**: Implemented in commit after ad768e7

**Shortcut**: `Ctrl+F3`

Full-text search across all files in the project, similar to VS Code's search functionality.

### Features

- **Search input** with options:
  - Case sensitive toggle (Aa)
  - Whole word toggle (W)
  - Regex mode toggle (.*)
- **File filters**:
  - Include patterns (e.g., `*.cs`, `src/**`)
  - Exclude patterns (e.g., `node_modules`, `bin`)
  - Respect .gitignore toggle
- **Results display**:
  - Grouped by file with expand/collapse
  - Context lines around matches (configurable)
  - Match highlighting in results
  - Click to open file at line
  - Result count per file and total
- **Replace functionality**:
  - Replace input field
  - Replace in file / Replace all in file
  - Replace all across project
  - Preview changes before applying
- **Performance**:
  - Incremental search with debouncing
  - Cancel ongoing search
  - Progress indicator for large projects
  - Max results limit (configurable)

### UI Layout

```
+-------------------------------------------------------------+
| Search in Files                                    [X Close] |
+-------------------------------------------------------------+
| [Search term________________] [Aa] [W] [.*]                 |
| [Replace with______________]                   [Replace All] |
+-------------------------------------------------------------+
| Include: [*.cs, *.xaml______]                               |
| Exclude: [bin, obj, node_modules, .git]  [ ] Use .gitignore |
+-------------------------------------------------------------+
| 23 results in 8 files                            [Searching] |
+-------------------------------------------------------------+
| v src/App.cs (5 matches)                                    |
|     12: public class [App]lication                          |
|     45: private [App]Config _config;                        |
|     ...                                                     |
| v src/Services/AppService.cs (3 matches)                    |
|     8: using [App].Domain;                                  |
|     ...                                                     |
| > src/ViewModels/MainViewModel.cs (2 matches)               |
+-------------------------------------------------------------+
```

### Configuration

```json
{
  "settings": {
    "searchDefaultInclude": "",
    "searchDefaultExclude": "bin,obj,node_modules,.git",
    "searchUseGitignore": true,
    "searchContextLines": 1,
    "searchMaxResults": 10000
  }
}
```

### Implementation Notes

- Use background thread for search operations
- Consider using ripgrep (rg) for performance if available
- Cache file list for faster subsequent searches
- Debounce input (300ms) before triggering search

### Command Palette Commands

| Command | Description |
|---------|-------------|
| Search: Find in Files | Open search panel (Ctrl+F3) |
| Search: Replace in Files | Open search panel with replace |
| Search: Clear Results | Clear current search results |

---

## Terminal Output Search (Medium Priority)

Search within terminal scrollback buffer to find previous output.

### Features

- **Search bar** in terminal toolbar
  - Appears with Ctrl+F when terminal is focused
  - Input field with Next/Previous buttons
  - Match count display (N of M)
  - Close button (Escape)
- **Match highlighting**:
  - All matches highlighted in terminal
  - Current match with distinct color
  - Scroll to match position
- **Navigation**:
  - F3 / Shift+F3 for next/previous
  - Enter for next match
  - Wrap around option
- **Options**:
  - Case sensitive toggle
  - Regex support (optional)

### UI Layout

```
+-------------------------------------------------------------+
| Custom Terminal                              [Search] [Links]|
+-------------------------------------------------------------+
| +--- Search: [pattern____] [<] [>] 3 of 12 [Aa] [X] -------+|
+-------------------------------------------------------------+
| $ npm install                                               |
| added 245 packages                                          |
| $ npm run build                                             |
| > project@1.0.0 build                                       |
| > webpack --mode production                                 |
|                                                             |
| [MATCH] Building for production...                          |
|                                                             |
+-------------------------------------------------------------+
```

### Implementation Notes

- Access terminal buffer via EasyTerminalControl API
- May need to intercept terminal output for indexing
- Consider limiting search to recent N lines for performance
- Highlight using terminal escape codes or overlay

---

## Snippet Manager (Low Priority)

Save and quickly insert code snippets for common patterns.

### Features

- **Snippet library**:
  - Global snippets (available in all projects)
  - Project-specific snippets
  - Built-in snippets for common patterns
- **Snippet properties**:
  - Name/label for quick identification
  - Description/documentation
  - Content (with placeholder support)
  - Keyboard shortcut (optional)
  - Target: terminal or file editor
  - Language/file type association
- **Placeholder support**:
  - `$1`, `$2`, etc. for tab stops
  - `${1:default}` for default values
  - `$CLIPBOARD` for clipboard content
  - `$DATE`, `$TIME` for timestamps
  - `$FILENAME`, `$DIRECTORY` for context
- **Quick insert**:
  - Command palette: "Insert Snippet: ..."
  - Keyboard shortcut
  - Context menu in file explorer
- **Snippet editor**: Create/edit snippets in settings

### UI Layout (Snippet Editor in Settings)

```
+-------------------------------------------------------------+
| Snippets                           [+ New] [Import] [Export]|
+-------------------------------------------------------------+
| Global Snippets                                             |
|   Console.WriteLine     C# console output                   |
|   Try-Catch Block       C# exception handling               |
|                                                             |
| Project Snippets                                            |
|   API Endpoint          Standard API controller method      |
+-------------------------------------------------------------+
| Edit: Console.WriteLine                                     |
| +-----------------------------------------------------------+
| | Name: [Console.WriteLine___________]                      |
| | Description: [C# console output____]                      |
| | Shortcut: [Ctrl+Shift+L____________]                      |
| | Target: [Terminal v]                                      |
| | Content:                                                  |
| | +-------------------------------------------------------+ |
| | | Console.WriteLine($"${1:message}");                   | |
| | +-------------------------------------------------------+ |
| +-----------------------------------------------------------+
|                                    [Delete] [Save]          |
+-------------------------------------------------------------+
```

### Configuration

```json
{
  "snippets": {
    "global": [
      {
        "id": "csharp-console",
        "name": "Console.WriteLine",
        "description": "C# console output",
        "content": "Console.WriteLine($\"${1:message}\");",
        "shortcut": "Ctrl+Shift+L",
        "target": "Terminal",
        "language": "csharp"
      }
    ],
    "project": {}
  }
}
```

### Command Palette Commands

| Command | Description |
|---------|-------------|
| Snippets: Insert... | Show snippet picker |
| Snippets: New Global | Create new global snippet |
| Snippets: New Project | Create new project snippet |
| Snippets: Manage | Open snippet editor |

---

## Session Snapshots (Low Priority)

Save and restore complete workspace states for different work contexts.

### Features

- **Save session**:
  - Capture current state:
    - Open tabs and their order
    - Active tab
    - Terminal states (working directory)
    - Panel visibility and positions
    - File viewer state (open files)
    - Current task (if in focus mode)
  - Name the session
  - Optional description
- **Session list**:
  - View all saved sessions
  - Last used timestamp
  - Quick description
  - Delete sessions
- **Restore session**:
  - Load saved session state
  - Option to merge or replace current
  - Handle missing directories gracefully
- **Auto-save**:
  - Periodic session backup (configurable interval)
  - Auto-save on exit
  - Restore last session on startup option

### UI Layout (Session Manager)

```
+-------------------------------------------------------------+
| Sessions                                         [+ Save]   |
+-------------------------------------------------------------+
| Current Session                          [Save] [Save As]   |
|   5 tabs open, Task: Implement PR #123                      |
+-------------------------------------------------------------+
| Saved Sessions                                              |
| +-----------------------------------------------------------+
| | Feature Development           Last used: 2 hours ago     |
| | 3 tabs: ConHoster, ApiProject, Tests                      |
| |                               [Load] [Load (Merge)] [X]  |
| +-----------------------------------------------------------+
| | Bug Investigation             Last used: 1 day ago        |
| | 2 tabs: LogAnalyzer, MainApp                              |
| |                               [Load] [Load (Merge)] [X]  |
| +-----------------------------------------------------------+
| | Code Review                   Last used: 3 days ago       |
| | 4 tabs: PR-456-Branch...                                  |
| |                               [Load] [Load (Merge)] [X]  |
| +-----------------------------------------------------------+
+-------------------------------------------------------------+
```

### Configuration

```json
{
  "settings": {
    "autoSaveSession": true,
    "autoSaveInterval": 300,
    "restoreLastSession": false
  },
  "sessions": [
    {
      "id": "session-123",
      "name": "Feature Development",
      "description": "Working on new features",
      "createdAt": "2025-12-20T10:00:00Z",
      "lastUsedAt": "2025-12-24T08:00:00Z",
      "state": {
        "tabs": ["P:\\ConHoster", "P:\\ApiProject"],
        "activeTabIndex": 0,
        "panels": {
          "fileExplorer": { "visible": true, "width": 250 },
          "gitChanges": { "visible": false }
        },
        "currentTaskId": "task-456"
      }
    }
  ],
  "lastSession": { /* auto-saved state */ }
}
```

### Command Palette Commands

| Command | Description |
|---------|-------------|
| Session: Save | Save current session |
| Session: Save As... | Save with new name |
| Session: Load... | Show session picker |
| Session: Manage | Open session manager |

---

## External Tool Integration (Low Priority)

Configure external tools for diff, merge, and file editing.

### Features

- **External diff tool**:
  - "Open in External Diff" in diff viewers
  - Compare two files externally
  - Configure tool path and arguments
- **External merge tool**:
  - Use for conflict resolution
  - Three-way merge support
  - Configure tool path and arguments
- **External editor**:
  - "Open in External Editor" in file operations
  - Support line number navigation
  - Configure editor path and arguments
- **External terminal**:
  - "Open in External Terminal" option
  - Open folder in Windows Terminal, etc.

### Configuration

```json
{
  "settings": {
    "externalTools": {
      "diff": {
        "enabled": true,
        "path": "C:\\Program Files\\Beyond Compare\\bcomp.exe",
        "args": "\"$LOCAL\" \"$REMOTE\""
      },
      "merge": {
        "enabled": true,
        "path": "C:\\Program Files\\Beyond Compare\\bcomp.exe",
        "args": "\"$LOCAL\" \"$REMOTE\" \"$BASE\" \"$MERGED\""
      },
      "editor": {
        "enabled": true,
        "path": "code",
        "args": "-g \"$FILE\":$LINE:$COLUMN"
      },
      "terminal": {
        "enabled": true,
        "path": "wt",
        "args": "-d \"$DIRECTORY\""
      }
    }
  }
}
```

### Variable Substitution

| Variable | Description |
|----------|-------------|
| `$FILE` | Full file path |
| `$DIRECTORY` | Directory path |
| `$LINE` | Line number (1-based) |
| `$COLUMN` | Column number (1-based) |
| `$LOCAL` | Local file (ours) |
| `$REMOTE` | Remote file (theirs) |
| `$BASE` | Base file (common ancestor) |
| `$MERGED` | Output file for merge |

### UI Integration

- Buttons in diff viewer toolbar: "Open in External Diff"
- Context menu in file explorer: "Open in External Editor"
- Settings section for configuring tools with path picker

---

## Environment Variable Manager (Low Priority)

Manage environment variables for run configurations.

### Features

- **Per-project env config**:
  - Key-value editor with add/remove/edit
  - Load from .env file
  - Export to .env file
  - Import from system environment
- **Environment sets**:
  - Multiple named configurations (dev, staging, prod)
  - Quick switch between sets
  - Inherit from other sets
- **Apply to run terminal**:
  - Set variables when starting run
  - Show active environment in status
- **Variable expansion**:
  - Reference other variables: `${VAR}`
  - Reference system variables: `${env:PATH}`

### UI Layout (Environment Editor in Settings)

```
+-------------------------------------------------------------+
| Environment Variables                    Project: ConHoster |
+-------------------------------------------------------------+
| Environment Set: [Development v]    [+ New Set] [Delete]    |
+-------------------------------------------------------------+
| Variables                                          [+ Add]  |
| +-----------------------------------------------------------+
| | NODE_ENV        = development              [Edit] [X]     |
| | API_URL         = http://localhost:3000    [Edit] [X]     |
| | DEBUG           = true                     [Edit] [X]     |
| +-----------------------------------------------------------+
|                                                             |
| [Load from .env] [Export to .env] [Import System]           |
+-------------------------------------------------------------+
```

### Configuration

```json
{
  "directorySettings": {
    "p:\\myproject": {
      "environments": {
        "development": {
          "NODE_ENV": "development",
          "API_URL": "http://localhost:3000",
          "DEBUG": "true"
        },
        "staging": {
          "NODE_ENV": "staging",
          "API_URL": "https://staging.example.com",
          "DEBUG": "false"
        },
        "production": {
          "NODE_ENV": "production",
          "API_URL": "https://api.example.com"
        }
      },
      "activeEnvironment": "development"
    }
  }
}
```

### .env File Support

Standard .env format:
```
# Comment
NODE_ENV=development
API_URL=http://localhost:3000

# Multiline (quoted)
PRIVATE_KEY="-----BEGIN RSA-----
...
-----END RSA-----"
```

---

## Implementation Priority

| Priority | Feature | Effort | Status |
|----------|---------|--------|--------|
| ~~**High**~~ | ~~Search Across Files~~ | ~~Medium~~ | **DONE** |
| **Medium** | Terminal Output Search | Medium | Pending |
| **Low** | Snippet Manager | Medium | Pending |
| **Low** | Session Snapshots | Medium | Pending |
| **Low** | External Tool Integration | Low | Pending |
| **Low** | Environment Variable Manager | Medium | Pending |

## Service Interfaces Required

### ISearchService

```csharp
public interface ISearchService
{
    Task<SearchResults> SearchAsync(
        string pattern,
        string workingDirectory,
        SearchOptions options,
        CancellationToken cancellationToken = default);

    Task<int> ReplaceAsync(
        string pattern,
        string replacement,
        string workingDirectory,
        SearchOptions options,
        CancellationToken cancellationToken = default);
}

public class SearchOptions
{
    public bool CaseSensitive { get; set; }
    public bool WholeWord { get; set; }
    public bool UseRegex { get; set; }
    public string? IncludePattern { get; set; }
    public string? ExcludePattern { get; set; }
    public bool UseGitignore { get; set; }
    public int ContextLines { get; set; }
    public int MaxResults { get; set; }
}

public class SearchResults
{
    public List<SearchMatch> Matches { get; set; }
    public int TotalCount { get; set; }
    public bool Truncated { get; set; }
}
```

### ISnippetService

```csharp
public interface ISnippetService
{
    IReadOnlyList<Snippet> GetGlobalSnippets();
    IReadOnlyList<Snippet> GetProjectSnippets(string projectPath);
    Task<string> ExpandSnippetAsync(Snippet snippet, Dictionary<string, string>? variables = null);
    Task SaveSnippetAsync(Snippet snippet, bool isGlobal);
    Task DeleteSnippetAsync(string snippetId, bool isGlobal);
}
```

### ISessionService

```csharp
public interface ISessionService
{
    IReadOnlyList<Session> GetSavedSessions();
    Task<Session> CaptureCurrentSessionAsync();
    Task SaveSessionAsync(Session session);
    Task DeleteSessionAsync(string sessionId);
    Task RestoreSessionAsync(string sessionId, bool merge = false);
}
```

---

*Document Version: 1.0*
*Created: 2025-12-24*
