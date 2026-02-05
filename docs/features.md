---
layout: default
title: Features
---

{% include nav.md %}

# Features

A comprehensive guide to TerminalHost features. Each section explains what the feature does and when to use it.

---

## Core Concepts

### Terminal Pairs

Every project tab in TerminalHost contains two terminals side-by-side: a **Custom terminal** (running your AI assistant, Claude Code by default) and a **Shell terminal** (PowerShell on Windows, zsh on macOS). This pairing lets you describe work to the AI while keeping a real shell ready for manual commands, tests, or git operations. Press `Ctrl+`` to instantly switch focus between them without terminating either process.

### Single-Instance with CLI

TerminalHost is designed around the `host` CLI command. Running `host .` from any directory will either open a new tab for that project or focus an existing tab if one is already open. This means you never accidentally create duplicate tabs for the same project, and you can quickly jump to any project from your terminal. The first instance of TerminalHost acts as a server; subsequent invocations communicate with it and exit.

### Session Persistence

When you close TerminalHost, it remembers your open tabs, window position, and per-directory settings (layout mode, split ratios, active terminal). On restart, everything is restored exactly as you left it. This persistence extends to individual project settings, so each directory can have its own preferred layout without affecting others.

---

## Tab Management

### Tab Navigation

Navigate between project tabs using `Ctrl+PageDown`/`Ctrl+PageUp` to cycle, or `Ctrl+1-9` to jump directly to a specific tab. For projects with many tabs, use the **Tab Switcher** (`Ctrl+Shift+T`) which provides fuzzy search across all open tabs. You can also drag tabs to reorder them, or middle-click to close.

### Tab Overflow & Dropdown

When you have more tabs than fit in the tab bar, an overflow dropdown appears showing all tabs. Click any item to switch to that tab. The dropdown also shows the full project path, useful when multiple projects share the same folder name.

---

## Terminal Features

### Layout Modes

Each project tab supports three layout modes: **Custom Full** (AI terminal fills the entire space), **Horizontal Split** (side-by-side), and **Vertical Split** (top-bottom). Toggle between them using the toolbar buttons or by dragging the splitter. Your layout preference is saved per-directory, so code-heavy projects can stay in split view while documentation projects use full mode.

### Run Terminal

Press `F5` to start your project's development server in a dedicated **Run terminal** that appears alongside your existing pair. TerminalHost auto-detects project types (.NET, Node.js, Python, Rust, Go) and runs the appropriate command. The run terminal scans output for localhost URLs and makes them clickable. Press `F5` again to stop, or `Shift+F5` to force-kill.

### Activity Indicators

Tabs display a spinning indicator when their terminals are actively producing output. This helps you spot which projects have running processes or recent activity without switching tabs. The indicator appears when output is received and fades after 2 seconds of silence.

### Detected Links

TerminalHost continuously scans terminal output for URLs and file paths. Click the **Links** button in the toolbar to see all detected links from the current terminal. URLs open in your default browser; file paths open in the built-in file viewer. You can also configure custom link patterns in Settings to match project-specific formats.

### Quick Commands

Quick Commands are configurable shortcuts that send predefined text to a terminal. The defaults include: `Ctrl+Shift+C` (send "commit" to Custom terminal), `Ctrl+Shift+D` (git pull), and `Ctrl+Shift+U` (git push). Define your own in Settings with custom text, target terminal, and keyboard shortcut. Quick Commands appear in the toolbar and Command Palette.

---

## Git Integration

### Git Status Display

Every project tab shows the current Git branch name and status in the tab header and status bar. You'll see indicators for uncommitted changes, ahead/behind counts relative to the remote, and whether you're in a detached HEAD state. This information updates automatically as you work.

### Git Changes Panel (Alt+G)

Open the Git Changes panel to see all modified, staged, and untracked files with inline diffs. Stage or unstage individual files by clicking the checkbox, or use the "Stage All" button. The panel includes a commit message editor with character count warnings and support for multi-line messages. After staging, write your message and click Commit without leaving the panel.

### Branch Switcher (Ctrl+B)

The Branch Switcher shows all local and remote branches with ahead/behind counts. Switch branches by clicking, or use the search box to filter. The panel also provides quick actions: create new branch, fetch from remote, pull with rebase, delete branch, and rename. For tracking branches, you'll see divergence information to help decide when to pull or push.

### Commit History (Ctrl+H)

Browse your repository's commit history with author, date, message, and file changes for each commit. Filter by author or search commit messages. The graph visualization shows branch topology. Right-click any commit to copy the hash, cherry-pick to current branch, or create a revert commit.

### Stash Manager (Ctrl+Shift+S)

View all stashes with their messages and dates. Apply a stash to restore changes without removing it from the list, or Pop to apply and delete. Drop removes a stash permanently. You can also create new stashes with custom messages, include untracked files, and create branches directly from stash contents.

### Reflog (Ctrl+Shift+G)

The Reflog shows all recent HEAD movements, including commits, resets, rebases, and branch switches. This is your safety net for recovering "lost" commits after a hard reset or failed rebase. From any reflog entry, you can checkout that state or create a new branch to preserve it.

### Branch Comparison (Ctrl+Alt+B)

Compare any two branches, tags, or commits side-by-side. See which commits are unique to each branch, the total file changes (+additions/-deletions), and browse individual file diffs. Useful before merging to understand what will change.

### Unified Git Panel (Alt+G)

A tabbed interface consolidating all Git features in one place: Branches, Changes, History, Stash, and Compare. The panel also includes a "Key Branches" section showing your current branch relative to development/production/staging with quick actions for fast-forward, reset, or compare operations.

---

## File Tools

### File Explorer (Ctrl+Shift+F)

Toggle a file tree panel showing your project's directory structure. Files display Git status indicators (modified, added, untracked) matching the colors in the Git Changes panel. Double-click to open files in the built-in viewer, or right-click for context menu options: open in external editor, copy path, reveal in Explorer, delete, or rename.

### File Viewer (Ctrl+O)

Open any file in a syntax-highlighted preview panel. Supports common programming languages, configuration formats, and images. The viewer is read-only by default, making it safe for quick inspection. Use `Ctrl+Shift+E` to open in edit mode instead. The viewer can be popped out into a separate window for side-by-side comparison.

### File Editor (Ctrl+Shift+E)

Open files for editing with syntax highlighting, line numbers, and unsaved changes tracking. The editor supports standard shortcuts (Ctrl+S to save, Ctrl+Z to undo) and warns before closing with unsaved changes. While not a full IDE, it's sufficient for quick config edits or small code changes without leaving TerminalHost.

### Search Across Files (Ctrl+F3)

Full-text search across all files in your project. Enter a search term and optionally filter by file extension or path pattern. Results show matching lines with context. Click any result to open that file at the matching line. The panel also supports search-and-replace with preview before applying changes.

### Markdown Preview (Ctrl+M)

Open Markdown files in a live-rendered preview panel. The preview updates automatically when the source file changes, making it useful for editing documentation while seeing the formatted result. Supports GitHub-flavored Markdown including code blocks, tables, and task lists.

---

## Project Runner

### Auto-Detection

When you press `F5`, TerminalHost examines your project directory to determine the appropriate run command. It recognizes: .NET projects (looks for .csproj, runs `dotnet run`), Node.js (package.json with scripts), Python (main.py or setup.py), Rust (Cargo.toml), and Go (go.mod). The detected command appears in the toolbar where you can also manually override it.

### Run Configurations

For projects that need custom run commands or environment variables, define a Run Configuration in Settings. Each configuration specifies the command, working directory, and any environment variables. You can create multiple configurations per project and select which one to use from the toolbar dropdown.

### URL Detection

The Run terminal monitors output for localhost URLs (like `http://localhost:3000` or `https://127.0.0.1:8080`). Detected URLs appear as clickable links in the terminal and in the status bar for one-click access to your running development server.

---

## GitHub Integration

### GitHub Dashboard (Ctrl+Shift+H)

A unified view of your GitHub activity across all repositories. See pull requests you've created, PRs awaiting your review, and assigned issues. Each item shows status, checks, and review state. Click any item to open it in the PR Review panel or your browser. Requires the `gh` CLI to be installed and authenticated.

### PR Review Mode (Ctrl+Shift+R)

Review pull requests without leaving TerminalHost. The panel shows the PR description, file changes with diffs, and existing comments. Add your review comments inline, then submit as Approve, Request Changes, or Comment. You can also checkout the PR branch directly to test changes locally.

### Repository Switcher (Ctrl+Shift+O)

Quickly switch between repositories using a searchable list. The switcher shows your favorites (pinned repos), recent projects, and repositories from your GitHub account. Select any item to open it as a new tab or focus an existing tab if already open.

---

## Productivity Features

### Command Palette (Ctrl+Shift+P)

Access any TerminalHost action through a searchable command list. The palette includes all menu items, Quick Commands, keyboard shortcuts, and Claude Code slash commands (if detected). Type to filter, then press Enter to execute. This is the fastest way to discover and use features without memorizing shortcuts.

### Scratch Pad (Ctrl+Shift+N)

A per-project notepad for temporary text, code snippets, or reminders. Content is saved automatically and persists across sessions. Use it to jot down ideas while working with the AI, store frequently-used commands, or draft commit messages before committing.

### Help Panel (F1)

Quick reference showing all keyboard shortcuts organized by category. The panel also includes links to documentation and support resources. Search within the help panel to find specific shortcuts or features.

### Toast Notifications

Non-intrusive notifications appear in the corner for operation feedback: successful commits, completed git operations, file saves, and errors. Progress toasts show ongoing operations like fetching from remote. Toasts auto-dismiss after a few seconds, or click to dismiss immediately.

---

## AI Assistant Support

### Multi-AI Support

TerminalHost works with any AI coding assistant that runs in a terminal. Built-in support includes Claude Code, Gemini CLI, GitHub Copilot CLI, and OpenAI Codex. Select which AI to use per-project from the toolbar dropdown. The selection persists per-directory, so different projects can use different assistants.

### Custom AI Configuration

Add your own AI assistants in Settings. Specify the command to launch, display name, and icon. Custom assistants appear in the toolbar dropdown alongside built-in options. This lets you integrate with any CLI-based AI tool or create shortcuts for specific AI configurations.

### Claude Commands Integration

TerminalHost automatically detects Claude Code slash commands from `~/.claude/commands/*.md` (global) and `.claude/commands/*.md` (per-project). Detected commands appear in the Command Palette with a "Claude: /" prefix. The file watcher updates the list automatically when you add or modify command files.

---

## Workspace & Layout

### Layout Modes (Ctrl+L)

Toggle between two major layout modes: **Tabs** (traditional tab bar at top) and **Sidebar** (project tree on the left). Tabs mode works like a standard tabbed interface. Sidebar mode shows all projects in a collapsible tree, useful when working with many related repositories or git worktrees.

### Workspace Sidebar

In Sidebar mode, the left panel displays your projects organized into sections: **Workspaces** (your main projects) and **Playground** (temporary experiments). Each entry shows the current branch, activity status, and ahead/behind counts. Right-click for context menu actions including git operations.

### Git Worktree Support

For repositories using git worktrees, TerminalHost displays worktrees as children of their parent project in the sidebar. Create new worktrees from the context menu, specifying branch name and target directory. Switch between worktrees instantly without losing your terminal state in other worktrees.

---

## Timeline Mode (Advanced)

### Overview (Ctrl+Shift+I)

Timeline Mode provides a visual representation of AI-assisted development work. It organizes work into **intents** (goals or features), each backed by a git worktree, with Claude Code sessions displayed as blocks on a horizontal timeline. This mode is designed for complex projects where you're exploring multiple approaches or working on several features in parallel.

### Intents and Sessions

An **intent** represents a development goal (like "implement authentication"). Each intent gets its own git worktree, so you can work on multiple features without branch switching. **Sessions** are individual Claude Code interactions within an intent, shown as blocks on the timeline. Sessions track duration, files changed, and commands executed.

### Forking and Exploration

From any completed session, you can **fork** to try an alternative approach. Forked sessions appear as parallel tracks in the timeline. If one approach fails, the other branches remain intact. This visual history helps you understand which approaches worked and why, making it easier to learn from AI-assisted development.

---

## Settings & Configuration

### Settings Editor (Ctrl+,)

The Settings panel provides two modes: **Rich** (form-based UI for common options) and **Raw** (direct JSON editing for advanced configuration). Changes save automatically. Settings include: terminal commands, appearance options, Quick Commands, link patterns, and per-directory preferences.

### Profile Management (Ctrl+P)

Profiles are named terminal configurations for different use cases. Create profiles for different AI assistants, shell configurations, or project types. Assign keyboard shortcuts to profiles for quick switching. Profiles can override the command, working directory, and environment variables.

### Configuration File

All settings are stored in a single JSON file:
- **Windows**: `%APPDATA%\TerminalHost\config.json`
- **macOS**: `~/.config/TerminalHost/config.json`

The file is human-readable and can be edited directly, backed up, or shared between machines. TerminalHost creates automatic backups before significant changes.

---

## System Integration

### System Tray

Enable "Show in System Tray" in Settings to keep TerminalHost running in the background when you close the window. Click the tray icon to restore the window, or right-click for quick actions. This is useful if you want TerminalHost always available without keeping the window open.

### Setup & Dependency Checker

Run `host /setup` to launch the Setup window, which verifies that all recommended dependencies are installed (Claude Code, PowerShell/zsh, Git, gh CLI). The setup runs automatically on first launch and can be skipped with `host --no-setup`. It provides download links for any missing dependencies.

---

*For keyboard shortcut reference, see [SHORTCUTS.md](../SHORTCUTS.md). For developer documentation, see [CLAUDE.md](../CLAUDE.md).*
