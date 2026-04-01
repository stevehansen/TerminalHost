# Platform-Specific Code Reference

This document catalogs all macOS and Linux-specific code in the codebase.

## Shared POSIX (`src/TerminalHost.Posix/`)

| File | Purpose |
|------|---------|
| `Services/PosixPtyServiceBase.cs` | Abstract base for PTY services. PATH construction, NVM detection, env vars (TERM, COLORTERM, HOME, SHELL), command resolution. Standard paths: `~/.local/bin`, `/usr/local/bin`, `/usr/bin`, `/bin`, `~/.cargo/bin`, `~/.npm-global/bin`. |
| `Services/PosixSingleInstanceService.cs` | Named pipe IPC (`$TMPDIR/TerminalHost_IPC_Pipe`), file-based lock fallback. |

---

## macOS

### Native Interop

**`src/TerminalHost.Avalonia/Services/MacOsDockHelper.cs`**
P/Invoke into `libobjc.dylib` — hooks `applicationShouldHandleReopen:hasVisibleWindows:` for dock icon click → window restore. Registers ObjC delegate via runtime selectors.

### Platform Services (`src/TerminalHost.macOS/Services/`)

| File | Purpose |
|------|---------|
| `MacPtyService.cs` | Extends `PosixPtyServiceBase`. Adds `/opt/homebrew/bin` (Apple Silicon). Rejects invalid dirs (`/Volumes/`, `.app/Contents/`). Default shell: `/bin/zsh`. |
| `MacSystemInfoService.cs` | Config: `~/Library/Application Support/TerminalHost/`. Fonts via `system_profiler SPFontsDataType`. Fallbacks: SF Mono, Menlo, Monaco. |

### PTY Syscalls (`lib/PtySharp/PtySharp.macOS/`)

| File | Purpose |
|------|---------|
| `MacOSPtySyscalls.cs` | P/Invoke to `libc` for `openpty()`. Terminal resize via `stty` subprocess on ARM64 (variadic ioctl calling convention mismatch). |
| `PtySession.cs` | macOS PTY session with `/dev/ttys*` device paths. |

### Conditional Compilation (`#if MACOS`)

| File | What it guards |
|------|----------------|
| `App.axaml.cs` | DI: `SystemInfoService`, container config path (`~/Library/Application Support/`) |
| `Controls/MacTerminalControl.cs` | `using TerminalHost.macOS.Services` and `new MacPtyService()` |

### Runtime Checks

**`MainWindow.axaml.cs`**
- `SetupMacOSMenu()` — Native menu bar: Cmd+N, Cmd+W, Cmd+C/V/A.
- Ctrl → Cmd (Meta) keyboard modifier translation (3 locations: general commands, profile shortcuts, terminal tab switching).

**`Services/ProcessService.cs`** — `open` for files/URLs, `open -R` for Finder reveal.

**`Views/SettingsView.axaml.cs`** — `open` for URLs and folders.

### Terminal Performance (`Controls/MacTerminalControl.cs`)

| Arch | Font | Render Throttle | Reason |
|------|------|-----------------|--------|
| Intel (x64) | Menlo | 50ms | Avoids Rosetta overhead |
| Apple Silicon (arm64) | Cascadia Code NF | 33ms | Full native performance |

### UI Workarounds

**`Controls/DraggablePopup.axaml`** — Inline draggable Border instead of native Popup (macOS rendering issues).

### Build & Deployment

**`scripts/build-macos.sh`** — Arch detection (arm64/x64), `dotnet publish --self-contained`, `.app` bundle, code signing (ad-hoc or identity), optional DMG via `hdiutil`.

**`TerminalHost.entitlements`** — Disables sandbox, allows JIT, unsigned executable memory, disables library validation.

**`Info.plist`** — `LSMinimumSystemVersion: 12.0`, dark mode (`NSRequiresAquaSystemAppearance: false`), category `public.app-category.developer-tools`, icon `Resources/app.icns`.

---

## Linux

### Platform Services (`src/TerminalHost.Linux/Services/`)

| File | Purpose |
|------|---------|
| `LinuxPtyService.cs` | Extends `PosixPtyServiceBase`. Adds `/snap/bin` (Snap support). Default shell: `/bin/bash` or `/bin/sh`. |
| `LinuxSystemInfoService.cs` | Config: `~/.config/TerminalHost/` (XDG). Fonts via `fc-list :spacing=100 family`. Fallbacks: DejaVu Sans Mono, Liberation Mono, Ubuntu Mono, Noto Sans Mono, Fira Code, JetBrains Mono, Hack, Inconsolata. |

### PTY Syscalls (`lib/PtySharp/PtySharp.Linux/`)

| File | Purpose |
|------|---------|
| `LinuxPtySyscalls.cs` | P/Invoke to `libutil` for `openpty()`. ioctl constant `TIOCSWINSZ = 0x5414`. Note: assumes glibc; may fail on musl (Alpine) where openpty is in libc. |
| `PtySession.cs` | Linux PTY session. Uses `setsid --ctty --fork` for controlling terminal. Device path: `/dev/pts/N`. |

### Conditional Compilation (`#if LINUX`)

| File | What it guards |
|------|----------------|
| `App.axaml.cs` | DI: `LinuxSystemInfoService`, container config path (`~/.config/`) |
| `Controls/MacTerminalControl.cs` | `using TerminalHost.Linux.Services` and `new LinuxPtyService()` |

### Runtime Checks

**`Services/ProcessService.cs`** — `xdg-open` for folders. `xdg-open` for file reveal (no file selection support unlike macOS/Windows).

**`Views/SettingsView.axaml.cs`** — `xdg-open` for URLs and folders.

### Terminal Fonts (`Controls/MacTerminalControl.cs`)

Linux font fallback chain:
1. Nerd Fonts: Cascadia Code NF, JetBrainsMono NF, FiraCode NF, Hack NF, MesloLGS NF
2. System monospace: DejaVu Sans Mono, Liberation Mono, Ubuntu Mono, Noto Sans Mono, Fira Mono, Source Code Pro, Inconsolata, Droid Sans Mono
3. Emoji: Noto Color Emoji, Segoe UI Emoji, Symbola
4. Math: DejaVu Math TeX Gyre, STIX Two Math

### Container Support (`src/TerminalHost.Core/Services/ContainerService.cs`)

- Clipboard bridging: symlinks `xclip`, `xsel`, `wl-paste`, `wl-copy` in container, bridges to host via named pipes.
- Path fixups: Windows absolute paths → Linux paths, CRLF → LF (prevents broken shebangs).
- Strips Windows-specific git config (`autocrlf`, `safecrlf`).

### Build

- Runtime identifiers: `linux-x64`, `linux-arm64`
- No dedicated build script (uses `dotnet publish` directly)
- No Linux CI workflow (currently Windows-only in `.github/workflows/dotnet.yml`)
