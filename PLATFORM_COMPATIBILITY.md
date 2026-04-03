# Platform Compatibility

Feature availability across Windows (WPF), macOS (Avalonia), and Linux (Avalonia).

**Legend:** Y = supported, -- = not implemented, Stub = interface exists but no-op

## Terminal & Core

| Feature               | Windows                  | macOS                                        | Linux                     |
| --------------------- | ------------------------ | -------------------------------------------- | ------------------------- |
| Terminal emulation    | ConPTY                   | VtNetCore + PtySharp                         | VtNetCore + PtySharp      |
| PTY resize            | Native ConPTY            | `stty` subprocess                            | Native POSIX ioctl        |
| Default shell         | pwsh.exe                 | /bin/zsh                                     | /bin/bash                 |
| Single instance (IPC) | Mutex + Named Pipes      | POSIX Mutex + Named Pipes                    | POSIX Mutex + Named Pipes |
| Config location       | `%APPDATA%\TerminalHost` | `~/Library/Application Support/TerminalHost` | `~/.config/TerminalHost`  |
| Git integration       | Y                        | Y                                            | Y                         |
| REST API and webhooks | Y                        | Y                                            | Y                         |
| Docker containers     | Y                        | Y                                            | Y                         |
| Channel bridge (MCP)  | Y                        | Y                                            | Y                         |

## UI & Desktop Integration

| Feature                     | Windows                 | macOS               | Linux                         |
| --------------------------- | ----------------------- | ------------------- | ----------------------------- |
| System tray                 | Y (NotifyIcon)          | Stub                | Stub                          |
| Dark mode detection         | Y (dwmapi.dll P/Invoke) | --                  | --                            |
| Taskbar progress            | Y (ITaskbarList3 COM)   | --                  | --                            |
| Dock icon click restore     | N/A                     | Y (ObjC interop)    | N/A                           |
| Toast notifications         | Y (custom WPF window)   | Y (Avalonia in-app) | Y (Avalonia in-app)           |
| Native system notifications | --                      | --                  | --                            |
| UI thread watchdog          | Y                       | --                  | --                            |
| File opening                | explorer.exe            | `open`              | `xdg-open`                    |
| Reveal in file manager      | Y (Explorer select)     | Y (Finder reveal)   | Partial (opens parent folder) |

## Voice, Audio & Spark Canvas

| Feature             | Windows           | macOS                | Linux                      |
| ------------------- | ----------------- | -------------------- | -------------------------- |
| Voice commands (F4) | Y (System.Speech) | --                   | --                         |
| Whisper engine      | Y (Whisper.net)   | --                   | --                         |
| Sound notifications | Y (NAudio)        | --                   | --                         |
| Spark Canvas        | N/A (WPF-only)    | Y (embedded WebView) | Partial (browser fallback) |

## Hook Installation

| Feature           | Windows               | macOS                  | Linux                      |
| ----------------- | --------------------- | ---------------------- | -------------------------- |
| Claude Code hooks | Y (`host.exe --hook`) | Y (`curl` to REST API) | -- (needs curl-based impl) |

## Native Dependencies

| Platform | Dependencies                                                                                                                                    |
| -------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| Windows  | `dwmapi.dll`, `user32.dll`, `gdi32.dll` (P/Invoke); EasyWindowsTerminalControl, System.Speech, NAudio, Whisper.net, Hardcodet.NotifyIcon.Wpf    |
| macOS    | `/usr/lib/libobjc.dylib` (ObjC runtime for dock handler); PtySharp.macOS                                                                        |
| Linux    | PtySharp.Linux; X11: libX11, libXi, libXcursor, libXrandr, libXext, libICE, libSM, libGL; font discovery via `fc-list`; Nix flake provides deps |

## Key Gaps for Linux

### High

- **Hook installer missing** — no hook installer service; needs curl/HTTP-based implementation matching the macOS approach
- **System tray stub** — no tray icon; needs D-Bus AppIndicator or Avalonia.Native implementation

### Medium

- **Voice commands** — no speech recognition on macOS or Linux; Whisper engine could be wired up cross-platform
- **Sound notifications** — no audio playback outside Windows
- **Spark Canvas in-app** — falls back to browser; no embedded WebView

### Low

- **Dark mode detection** — not implemented on macOS or Linux (Avalonia uses its own theming)
- **Taskbar progress** — not implemented on macOS or Linux
- **Native system notifications** — could use D-Bus `org.freedesktop.Notifications`
- **Reveal in file manager** — can only open the parent folder, not highlight the specific file
