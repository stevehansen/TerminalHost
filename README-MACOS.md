# TerminalHost for macOS

A terminal host application for managing project terminals with Claude Code integration.

## Requirements

- macOS 12.0 or later
- .NET 8.0 Runtime (bundled in self-contained build)

## Installation

### From Release
1. Download `TerminalHost.app.zip`
2. Extract and drag to Applications
3. Right-click and select "Open" (first time only, due to Gatekeeper)

### From Source
```bash
git clone <repo>
cd TerminalHost
./build-macos.sh
```

The app bundle will be created at `publish/TerminalHost.app`.

## Usage

```bash
# Open app
open /Applications/TerminalHost.app

# Open with specific project
open /Applications/TerminalHost.app --args ~/my-project

# From source build
open publish/TerminalHost.app
```

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Cmd+N | New Terminal |
| Cmd+W | Close Window |
| Cmd+L | Clear Terminal |
| Cmd+C | Copy |
| Cmd+V | Paste |
| Cmd+A | Select All |
| Cmd+Ctrl+F | Toggle Full Screen |
| Cmd+M | Minimize |
| F1 | Help / Keyboard Shortcuts |

## Configuration

Configuration is stored in:
```
~/Library/Application Support/TerminalHost/config.json
```

## Building

### Prerequisites
- .NET 8.0 SDK
- Xcode Command Line Tools (for codesigning)

### Build Commands
```bash
# Quick build (debug)
dotnet build

# Release build with app bundle
./build-macos.sh

# Manual publish
dotnet publish src/TerminalHost/TerminalHost -c Release -r osx-arm64 -o publish/osx-arm64
```

### Code Signing (Optional)

For distribution outside the App Store:

```bash
# Set your code signing identity
export CODESIGN_IDENTITY="Developer ID Application: Your Name"

# Build with code signing
./build-macos.sh
```

### Notarization (Optional)

For distribution:

```bash
# Create ZIP for notarization
ditto -c -k --keepParent "publish/TerminalHost.app" "TerminalHost.zip"

# Submit for notarization
xcrun notarytool submit "TerminalHost.zip" \
    --apple-id "your@email.com" \
    --password "app-specific-password" \
    --team-id "TEAM_ID" \
    --wait

# Staple ticket
xcrun stapler staple "publish/TerminalHost.app"
```

## Known Issues

- First launch may require right-click > Open due to Gatekeeper
- Font tests require Avalonia runtime (skipped in unit tests)

## Technology Stack

- **Framework**: .NET 8.0 with Avalonia UI
- **Terminal**: XtermSharp + Pty.Net
- **Theme**: Fluent Dark

## License

See LICENSE file in the repository.
