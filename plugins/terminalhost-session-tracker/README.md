# TerminalHost Session Tracker Plugin

<!-- Hook test: 00:52 -->

A Claude Code plugin that enables automatic session tracking for TerminalHost's Timeline Mode.

## What It Does

This plugin notifies TerminalHost when:
- A Claude Code session **starts** (SessionStart hook)
- Files are **modified** via Write/Edit/MultiEdit tools (PostToolUse hook)
- A Claude Code session **ends** (Stop hook)

This data is used to populate the Timeline Mode UI with session blocks, showing:
- Session duration
- Files changed
- Git commits made
- Session status (running/success/failed)

## Requirements

- **TerminalHost** must be installed with `host.exe` in your PATH
- **Claude Code** version 1.0.0 or higher

## Installation

### Option 1: Add as Local Marketplace

```bash
# Add the plugin directory as a marketplace
/plugin marketplace add P:\ConHoster\plugins\terminalhost-session-tracker

# Then install the plugin
/plugin install terminalhost-session-tracker@terminalhost-plugins
```

### Option 2: Via TerminalHost UI

1. Open TerminalHost
2. Go to **Settings** (Ctrl+,)
3. Navigate to **Timeline Mode** section
4. Click **Install Session Tracking Hooks**

### Option 3: From GitHub (after pushing)

```bash
# Add the GitHub repo as a marketplace
/plugin marketplace add stevehansen/TerminalHost

# Install the plugin
/plugin install terminalhost-session-tracker@TerminalHost
```

## Verification

After installation, verify the hooks are active:

```bash
/plugin list
```

You should see `terminalhost-session-tracker` in the list.

## How It Works

1. When you start a Claude Code session in a directory that matches a Timeline Mode **Intent**, the plugin sends session info to TerminalHost via `host.exe --hook session-start`

2. As Claude modifies files, each Write/Edit/MultiEdit operation triggers a `file-changed` hook

3. When the session ends (via `/exit`, Ctrl+C, or completion), the `session-stop` hook is triggered

4. TerminalHost correlates these events with git data to show commits, file stats, and session outcomes

## Offline Support

If TerminalHost isn't running when hooks fire:
- Events are queued to `%APPDATA%\TerminalHost\hook-queue.jsonl`
- Queued events are processed when TerminalHost starts

## Uninstallation

```bash
claude /plugin uninstall terminalhost-session-tracker
```

Or via TerminalHost Settings → Timeline Mode → **Uninstall**.

## Troubleshooting

### Hooks not firing

1. Ensure `host.exe` is in your PATH:
   ```bash
   where host.exe
   ```

2. Check plugin is enabled:
   ```bash
   claude /plugin list
   ```

3. Verify hooks configuration:
   ```bash
   claude /plugin info terminalhost-session-tracker
   ```

### Sessions not appearing in Timeline

1. Ensure you're working in an **Intent worktree** directory
2. Check Timeline Mode is enabled in TerminalHost settings
3. Look for queued events in `%APPDATA%\TerminalHost\hook-queue.jsonl`

## Links

- [TerminalHost Repository](https://github.com/stevehansen/TerminalHost)
- [Timeline Mode Specification](https://github.com/stevehansen/TerminalHost/blob/master/docs/specs/TimelineIDE.md)

## License

MIT
