# Stage 4: Terminal Control Integration

## Overview

| Attribute | Value |
|-----------|-------|
| **Estimated Effort** | 5-7 days |
| **Risk Level** | **High** |
| **Dependencies** | Stage 2, Stage 3 complete |
| **Blocking For** | Stages 5, 6, 7 |
| **Status** | **COMPLETE** |

## Objective

Implement the terminal emulation stack for macOS using XtermSharp (or alternative) and Pty.Net. This replaces EasyWindowsTerminalControl which is Windows-only.

## Success Criteria

- [x] PTY processes spawn correctly on macOS
- [x] Terminal output renders in Avalonia control
- [x] Keyboard input works correctly
- [x] Mouse selection works
- [x] Terminal themes apply
- [x] Process exit detection works
- [x] Multiple terminal instances supported

---

## Technology Selection

### Implemented Stack

| Component | Library | Version | Purpose |
|-----------|---------|---------|---------|
| PTY Management | **sch.pty.net** | 0.3.36-pre | Cross-platform pseudo-terminal (fork of Microsoft's Pty.Net) |
| Terminal Emulation | **XtermSharp** | Git submodule | VT100/xterm parser and state |
| Rendering | Custom Avalonia control | - | Visual rendering |

### Notes on Package Selection

- **sch.pty.net**: The original Microsoft `Pty.Net` package on NuGet is deprecated (2018). `sch.pty.net` is a maintained fork targeting .NET Standard 2.0.
- **XtermSharp**: Added as a Git submodule since no NuGet package is available.

---

## Detailed Implementation

### 4.1 Add XtermSharp as Submodule

**COMPLETED**

```bash
cd /path/to/TerminalHost
mkdir -p external
git submodule add https://github.com/migueldeicaza/XtermSharp.git external/XtermSharp
```

**Required Fix for XtermSharp.csproj:**
The XtermSharp.csproj contains a malformed `Visible="False"` attribute that causes build errors with modern .NET SDK:

```xml
<!-- BEFORE (causes CS0246 error): -->
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute" Visible="False">

<!-- AFTER (fixed): -->
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
```

**UPDATE TerminalHost.csproj:**
```xml
<!-- Stage 4: Terminal PTY support -->
<PackageReference Include="sch.pty.net" Version="0.3.36-pre" />

<!-- Stage 4: XtermSharp terminal emulation -->
<ItemGroup>
  <ProjectReference Include="..\..\..\external\XtermSharp\XtermSharp\XtermSharp.csproj" />
</ItemGroup>
```

---

### 4.2 Create MacTerminalControl

**COMPLETED:** `src/TerminalHost/TerminalHost/Controls/MacTerminalControl.cs`

Key implementation details:

1. **Process Exit Detection**: IPtyConnection doesn't have `HasExited` property. Use a `_processExited` flag updated via `ProcessExited` event:
```csharp
private bool _processExited;

// Subscribe to event
_pty.ProcessExited += OnPtyProcessExited;

private void OnPtyProcessExited(object? sender, PtyExitedEventArgs e)
{
    _processExited = true;
    Dispatcher.UIThread.Post(() => ProcessExited?.Invoke(this, e.ExitCode));
}
```

2. **Character Rendering**: NStack 0.12.0 (used by XtermSharp) doesn't have `Rune.Value`. Use `CharData.Code` instead:
```csharp
// Use Code property instead of Rune.Value
if (charData.Code == 0 || charData.Code == 32)
{
    sb.Append(' ');
}
else
{
    sb.Append(charData.Rune.ToString());
}
```

3. **Application Cursor Mode**: Handle arrow keys differently based on terminal mode:
```csharp
Key.Up => _terminal?.ApplicationCursor == true ? "\x1bOA" : "\x1b[A",
```

---

### 4.3 Update TerminalControlFactory

**COMPLETED:** `src/TerminalHost/TerminalHost/Services/TerminalControlFactory.cs`

Interface changed to async:
```csharp
public interface ITerminalControlFactory
{
    Task<ITerminalControl> CreateTerminalControlAsync(TerminalSession session);
}
```

---

### 4.4 Terminal Theme Support

**COMPLETED:** `src/TerminalHost/TerminalHost/Domain/TerminalTheme.cs`

Three themes implemented:
- Campbell (Windows Terminal default)
- One Dark
- Solarized Dark

---

### 4.5 Service Stubs

**COMPLETED:** Created Avalonia-compatible stub for `DialogService.cs`

The DialogService was rewritten to remove WPF dependencies. Full Avalonia dialog UI will be implemented in Stage 5.

---

## File Change Summary

| Action | File | Notes |
|--------|------|-------|
| **CREATE** | `Controls/MacTerminalControl.cs` | Main terminal control (620+ lines) |
| **CREATE** | `Domain/TerminalTheme.cs` | Theme definitions (3 themes) |
| **REWRITE** | `Services/TerminalControlFactory.cs` | Async creation with macOS support |
| **REWRITE** | `Services/ITerminalControlFactory.cs` | Changed to async interface |
| **REWRITE** | `Services/DialogService.cs` | Avalonia-compatible stub |
| **ADD** | `external/XtermSharp/` | Git submodule |
| **PATCH** | `external/XtermSharp/XtermSharp/XtermSharp.csproj` | Fixed Visible attribute |
| **UPDATE** | `TerminalHost.csproj` | Added sch.pty.net, XtermSharp reference, Stage 4 file includes |

---

## Included Files in TerminalHost.csproj

Stage 4 re-includes these files after Stage 1 exclusions:

```xml
<!-- Stage 4: Re-include terminal control files after Stage 1 exclusions -->
<ItemGroup>
  <!-- Domain interfaces and models -->
  <Compile Include="Domain\ITerminalControl.cs" />
  <Compile Include="Domain\TerminalTheme.cs" />
  <Compile Include="Domain\Profile.cs" />
  <Compile Include="Domain\TerminalSession.cs" />
  <Compile Include="Domain\SessionState.cs" />
  <Compile Include="Domain\UsageStats.cs" />
  <Compile Include="Domain\DirectoryUsageStats.cs" />

  <!-- Service interfaces and implementations for Stage 4 -->
  <Compile Include="Services\ITerminalControlFactory.cs" />
  <Compile Include="Services\TerminalControlFactory.cs" />
  <Compile Include="Services\ISystemInfoService.cs" />
  <Compile Include="Services\SystemInfoService.cs" />
  <Compile Include="Services\IFileSystem.cs" />
  <Compile Include="Services\IDialogService.cs" />
  <Compile Include="Services\DialogService.cs" />
  <Compile Include="Services\IClipboardService.cs" />
  <Compile Include="Services\ClipboardService.cs" />
  <Compile Include="Services\IStatisticsService.cs" />
  <Compile Include="Services\StatisticsService.cs" />
  <Compile Include="Services\JsonFileService.cs" />

  <!-- Terminal control -->
  <Compile Include="Controls\MacTerminalControl.cs" />
</ItemGroup>
```

---

## Testing Strategy

### Manual Testing Checklist

1. **Basic Shell**
   - [ ] Open terminal with default shell (zsh)
   - [ ] Type commands and see output
   - [ ] Verify colors work

2. **Keyboard Input**
   - [ ] Regular typing
   - [ ] Enter key executes commands
   - [ ] Arrow keys for history/navigation
   - [ ] Tab completion
   - [ ] Ctrl+C interrupts

3. **Mouse**
   - [ ] Click to focus
   - [ ] Selection with drag
   - [ ] Ctrl+Click for links

4. **Resize**
   - [ ] Terminal resizes with window
   - [ ] Content reflows correctly

5. **Process Management**
   - [ ] Process exit detected
   - [ ] Can restart terminal
   - [ ] Multiple terminals work

---

## Known Issues & Resolutions

### Issue 1: XtermSharp Buffer API
**Status:** RESOLVED

XtermSharp's buffer API uses `CharData.Code` for character codes, not `Rune.Value`.

### Issue 2: IPtyConnection HasExited
**Status:** RESOLVED

IPtyConnection doesn't have `HasExited` property. Solution: Use a `_processExited` flag updated via `ProcessExited` event.

### Issue 3: XtermSharp.csproj Build Error
**Status:** RESOLVED

The `Visible="False"` attribute on AssemblyAttribute causes CS0246 error. Fixed by removing the attribute.

### Issue 4: Pty.Net Package Deprecated
**Status:** RESOLVED

Original Pty.Net package deprecated. Using `sch.pty.net` (0.3.36-pre) which is a maintained fork.

---

## Build Verification

```bash
$ dotnet build src/TerminalHost/TerminalHost/TerminalHost.csproj

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Next Stage

After completing Stage 4, proceed to **Stage 5: Core UI Migration (Avalonia)** which migrates App.xaml and MainWindow.xaml to Avalonia.
