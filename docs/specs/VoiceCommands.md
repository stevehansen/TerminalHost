# PRD: Voice Commands

## Overview

Voice command support for TerminalHost, enabling hands-free invocation of existing palette commands, quick commands, and terminal actions via speech recognition. Leverages the existing command palette infrastructure as the action registry — voice becomes an alternative input method alongside keyboard shortcuts and the search palette.

## Problem Statement

TerminalHost has a rich set of actions available through keyboard shortcuts and the command palette, but all require keyboard/mouse input:
- **Context switching cost**: When reviewing code on a secondary display or whiteboarding, reaching for the keyboard interrupts flow
- **Accessibility**: Users with repetitive strain injuries or motor impairments need alternative input methods
- **Mobile RDP / Touch Mode**: Touch Mode already acknowledges non-traditional input — voice is a natural complement
- **Ambient control**: Quick commands like "push", "pull", "commit" are natural voice targets

## Goals

1. **Voice as an input method** — Invoke any existing command palette action or quick command by speaking
2. **Low-latency, local-first** — Prefer on-device recognition for common commands; optional cloud for complex phrases
3. **Discoverable** — Users can say "help" or "what can I say" to see available commands
4. **Non-intrusive** — Voice does not interfere with terminal input or AI assistant interaction
5. **Progressive enhancement** — Voice is opt-in; all existing keyboard/mouse flows remain unchanged

## Non-Goals

- Dictation / free-form text entry into terminals (Phase 1)
- Voice control of terminal content (scrolling, selecting text)
- Custom wake word training
- Real-time transcription display

## Architecture

> **Prerequisite addressed**: The command palette now contains all invocable actions (git operations, panel toggles, layout modes, settings toggles). Voice commands can rely on the palette as the complete action registry.

### Command Resolution Pipeline

```
Microphone → Speech Recognition → Transcript
    ↓
Transcript → Command Matcher → Matched Action
    ↓
Matched Action → Execute (same path as palette/shortcut)
```

Voice commands reuse the **exact same execution path** as the command palette. The `PaletteCommand.Execute` delegate, `QuickCommand` terminal injection, and `ClaudeCommand` slash commands are all invoked identically whether triggered by keyboard, palette click, or voice.

### Component Diagram

```
┌─────────────────────────────────────────────────────┐
│                    MainViewModel                     │
│                                                      │
│  ┌──────────────┐   ┌───────────────────────────┐   │
│  │ Command       │   │ VoiceCommandService        │   │
│  │ Palette       │──▶│                           │   │
│  │ (existing)    │   │ - ISpeechRecognitionEngine │   │
│  └──────────────┘   │ - ICommandMatcher          │   │
│                      │ - listening state          │   │
│                      └───────────────────────────┘   │
│                              │                       │
│                              ▼                       │
│                      ┌───────────────────┐           │
│                      │ Execute action     │           │
│                      │ (same as palette)  │           │
│                      └───────────────────┘           │
└─────────────────────────────────────────────────────┘
```

## Features

### Phase 1: Core Voice Commands (MVP)

#### 1.1 Push-to-Talk Activation

| Property | Value |
|----------|-------|
| Shortcut | `Ctrl+Shift+V` (hold) or configurable |
| Behavior | Hold to listen, release to process |
| Indicator | Microphone icon in toolbar pulses red while listening |
| Fallback | Toggle mode: press once to start, press again to stop |

> **Note**: `Ctrl+Shift+V` is currently "Review PR" quick command. May need to reassign or use a different default like `F4` or a dedicated mic button.

#### 1.2 Command Vocabulary

Voice commands map directly to existing actions. The matcher builds its vocabulary from the live command registry:

**Direct Palette Commands** (auto-registered from `InitializeCommandPalette()`):

| Voice Phrase | Palette Command ID | Action |
|---|---|---|
| "new project" / "open project" | `new-project` | Open folder picker |
| "close tab" | `close-tab` | Close current tab |
| "switch terminal" / "toggle terminal" | `switch-terminal` | Ctrl+` |
| "settings" / "open settings" | `settings` | Open settings |
| "command palette" | (self) | Open command palette |
| "git changes" / "git status" | `git-changes` | Open git changes panel |
| "branches" / "switch branch" | `git-branches` | Open branch switcher |
| "commit history" / "git log" | `git-history` | Open commit history |
| "git stash" | `git-stash` | Open stash manager |
| "file explorer" | `file-explorer` | Toggle file explorer |
| "scratch pad" / "notes" | `scratch-pad` | Open scratch pad |
| "help" / "what can I say" | `help` | Show help (with voice commands section) |
| "dashboard" | `dashboard` | GitHub dashboard |
| "review PR" / "PR review" | `pr-review` | PR review mode |
| "run" / "start project" | `run-start` | F5 |
| "stop" / "stop project" | `run-stop` | Shift+F5 |
| "timeline" | `timeline` | Open timeline |
| "search" / "find in files" | `file-search` | Ctrl+F3 |
| "markdown preview" | `markdown-preview` | Ctrl+M |

**Quick Commands** (auto-registered from config):

| Voice Phrase | Quick Command ID | Action |
|---|---|---|
| "commit" | `commit` | Send "commit" to Claude Code |
| "push" | (built-in) | Git push |
| "pull" / "pull rebase" | (built-in) | Git pull --rebase |

**Tab Navigation**:

| Voice Phrase | Action |
|---|---|
| "next tab" | Ctrl+PageDown |
| "previous tab" | Ctrl+PageUp |
| "tab one" / "tab two" / ... | Ctrl+1-9 |

#### 1.3 Command Matching Strategy

```
Transcript → Normalize → Fuzzy Match → Confidence Check → Execute
```

1. **Normalize**: Lowercase, strip filler words ("um", "uh", "please", "can you"), strip articles ("the", "a")
2. **Alias Lookup**: Each command has a primary name + aliases (e.g., "git status" = "git changes")
3. **Fuzzy Match**: Levenshtein distance or similar for minor speech-to-text errors
4. **Confidence Threshold**: Only execute if match confidence > 0.8; otherwise show top 3 matches for user to pick
5. **Ambiguity Resolution**: If multiple commands match equally, show disambiguation popup

#### 1.4 Voice Feedback

| Event | Feedback |
|---|---|
| Listening started | Toolbar mic icon pulses red; subtle audio chime (optional) |
| Command recognized | Toast: "Executed: {command name}" (Success type) |
| Low confidence | Toast with top matches: "Did you mean: ..." (Info type, no auto-close) |
| No match | Toast: "Didn't catch that. Try 'help' for commands." (Warning type) |
| Listening stopped | Mic icon returns to normal |
| Error (no mic access) | Toast: "Microphone access denied" (Error type) |

### Phase 2: Enhanced Voice Features

#### 2.1 Parameterized Commands

Commands that accept arguments:

| Voice Phrase | Action |
|---|---|
| "open tab {name}" | Fuzzy match tab by directory name |
| "switch to branch {name}" | Branch checkout |
| "commit {message}" | Send "commit" with message to Claude Code |
| "search for {query}" | Open search with pre-filled query |
| "run command {text}" | Send arbitrary text to shell terminal |

#### 2.2 Continuous Listening Mode

Optional always-listening mode with wake phrase:
- Wake phrase: "Hey Terminal" or "OK Host" (configurable)
- Timeout: Returns to sleep after 10s of no speech
- Visual: Persistent subtle mic indicator when in continuous mode

#### 2.3 Voice Dictation

Free-form text entry for terminals:
- "Type {text}" — sends text to active terminal
- "Enter" / "Return" — sends Enter key
- "Escape" — sends Escape key

### Phase 3: Advanced

#### 3.1 Custom Voice Aliases

Users can define custom voice triggers in settings:

```json
{
  "voiceCommands": {
    "aliases": {
      "deploy": { "action": "quick-command", "id": "my-deploy-cmd" },
      "test it": { "action": "quick-command", "id": "run-tests" },
      "nuke modules": { "action": "shell", "text": "rm -rf node_modules && npm install" }
    }
  }
}
```

#### 3.2 Context-Aware Commands

Commands that behave differently based on current state:
- "close" → close active panel if one is open, otherwise close tab
- "back" → close panel/popup, return to terminal
- "focus" → focus the active terminal

## Technical Design

### Speech Recognition Engine

#### Option A: Windows Speech Recognition (Recommended for Phase 1)

| Property | Value |
|---|---|
| API | `System.Speech.Recognition` (.NET) |
| Platform | Windows only |
| Latency | ~200-500ms |
| Accuracy | Good for constrained grammar (command vocabulary) |
| Privacy | Fully local, no network |
| Cost | Free |

**Advantages**: No dependencies, no API keys, constrained grammar mode means high accuracy for known commands, fully offline.

**Implementation**:
```csharp
// Core service interface (in TerminalHost.Core)
public interface IVoiceCommandService
{
    bool IsAvailable { get; }
    bool IsListening { get; }

    void StartListening();
    void StopListening();

    event EventHandler<VoiceCommandRecognizedEventArgs> CommandRecognized;
    event EventHandler<VoiceCommandErrorEventArgs> Error;
    event EventHandler ListeningStateChanged;
}

public class VoiceCommandRecognizedEventArgs : EventArgs
{
    public string Transcript { get; init; }
    public float Confidence { get; init; }
    public PaletteCommand? MatchedCommand { get; init; }
    public List<CommandMatch>? Alternatives { get; init; }
}

public record CommandMatch(PaletteCommand Command, float Confidence);
```

**Windows Implementation** (in TerminalHost.Windows):
```csharp
// Uses System.Speech.Recognition with constrained grammar
public class WindowsVoiceCommandService : IVoiceCommandService
{
    private SpeechRecognitionEngine _engine;
    private Grammar _commandGrammar;

    // Builds grammar from command palette + quick commands
    public void RebuildGrammar(IEnumerable<VoiceCommandEntry> commands) { }
}
```

#### Option B: Azure Cognitive Services Speech

| Property | Value |
|---|---|
| API | Azure Speech SDK |
| Platform | Cross-platform |
| Latency | ~500-1000ms (network) |
| Accuracy | Very high (neural models) |
| Privacy | Cloud-processed |
| Cost | Free tier: 5hrs/month; then $1/hr |

**When to use**: Phase 2+ for continuous listening, parameterized commands, or macOS support.

#### Option C: Whisper (Local)

| Property | Value |
|---|---|
| API | OpenAI Whisper via whisper.cpp |
| Platform | Cross-platform |
| Latency | ~1-3s (depends on model size) |
| Accuracy | Excellent |
| Privacy | Fully local |
| Cost | Free; ~1-4GB model download |

**When to use**: Phase 2+ for cross-platform local recognition with high accuracy.

### macOS Support

| Phase | Approach |
|---|---|
| Phase 1 | macOS voice commands deferred (Windows `System.Speech` only) |
| Phase 2 | `NSSpeechRecognizer` via Avalonia/native interop, or Whisper |

### Command Registry Integration

The voice command system reads from the same sources as the command palette:

```csharp
public interface ICommandVocabulary
{
    /// Returns all speakable commands with their voice aliases
    IReadOnlyList<VoiceCommandEntry> GetVoiceCommands();
}

public class VoiceCommandEntry
{
    public string CommandId { get; init; }          // Palette command ID
    public string PrimaryPhrase { get; init; }      // "git changes"
    public string[] Aliases { get; init; }          // ["git status", "show changes"]
    public Action Execute { get; init; }            // Same delegate as palette
    public string Category { get; init; }           // For "what can I say" grouping
}
```

**Registration** happens in `MainViewModel` alongside `InitializeCommandPalette()`:

```csharp
private void InitializeVoiceCommands()
{
    var entries = new List<VoiceCommandEntry>();

    // Auto-register all palette commands with name as primary phrase
    foreach (var cmd in _allPaletteCommands)
    {
        entries.Add(new VoiceCommandEntry
        {
            CommandId = cmd.Id,
            PrimaryPhrase = cmd.Name.ToLowerInvariant(),
            Aliases = GetVoiceAliases(cmd.Id),  // Curated alias map
            Execute = cmd.Execute,
            Category = cmd.Category
        });
    }

    // Auto-register quick commands
    foreach (var qc in config.QuickCommands)
    {
        entries.Add(new VoiceCommandEntry
        {
            CommandId = $"qc-{qc.Id}",
            PrimaryPhrase = qc.Label.ToLowerInvariant(),
            Aliases = [],
            Execute = () => ExecuteQuickCommand(qc),
            Category = "Quick Command"
        });
    }

    _voiceCommandService.RebuildGrammar(entries);
}
```

## Settings

New settings in `AppSettings`:

```json
{
  "settings": {
    "voiceCommandsEnabled": false,
    "voiceActivationMode": "push-to-talk",
    "voiceActivationShortcut": "F4",
    "voiceConfidenceThreshold": 0.8,
    "voiceFeedbackSounds": true,
    "voiceShowTranscript": true,
    "voiceCustomAliases": {}
  }
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `voiceCommandsEnabled` | bool | `false` | Master toggle |
| `voiceActivationMode` | enum | `push-to-talk` | `push-to-talk` or `toggle` |
| `voiceActivationShortcut` | string | `F4` | Keyboard shortcut to activate |
| `voiceConfidenceThreshold` | float | `0.8` | Minimum confidence to auto-execute |
| `voiceFeedbackSounds` | bool | `true` | Play chime on listen start/stop |
| `voiceShowTranscript` | bool | `true` | Show recognized text in toast |
| `voiceCustomAliases` | dict | `{}` | Custom voice phrase → command mappings |

## UI Changes

### Toolbar

New microphone button in the toolbar (after existing buttons):

```
[Mic 🎤] — Click to toggle listening (or hold for push-to-talk)
         — Red pulsing animation while listening
         — Grayed out if voice not enabled/available
         — Tooltip: "Voice Commands (F4)"
         — Hidden if voiceCommandsEnabled = false
```

### Settings View

New "Voice Commands" section:

```
┌─ Voice Commands ──────────────────────────────────┐
│                                                     │
│  [x] Enable voice commands                         │
│                                                     │
│  Activation mode:  ( ) Push-to-talk  (x) Toggle    │
│  Activation key:   [F4        ] [Record new]       │
│                                                     │
│  Confidence threshold: [====●====] 80%              │
│                                                     │
│  [x] Play feedback sounds                          │
│  [x] Show transcript in toast                      │
│                                                     │
│  [Manage voice aliases...]                         │
│                                                     │
└─────────────────────────────────────────────────────┘
```

### Help View

New section in F1 help showing voice commands:

```
Voice Commands (when enabled)
  F4              Start/stop voice listening
  "help"          Show available voice commands
  "commit"        Send commit to Claude Code
  "push"          Git push
  "pull"          Git pull --rebase
  "git changes"   Open git changes panel
  "branches"      Open branch switcher
  ...
```

### Command Palette

Add a "Voice" indicator to commands that have voice aliases, so users learn what they can say:

```
🎤 Git Changes          Alt+G    "git changes"
🔀 Switch Branch         Ctrl+B   "branches"
```

## Implementation Plan

### Phase 1 Milestones (MVP)

| # | Task | Estimate | Dependencies |
|---|------|----------|--------------|
| 1 | Define `IVoiceCommandService` interface in Core | S | None |
| 2 | Define `VoiceCommandEntry` model and `ICommandVocabulary` | S | None |
| 3 | Implement `WindowsVoiceCommandService` using System.Speech | M | #1 |
| 4 | Build command matcher with alias support + fuzzy matching | M | #2 |
| 5 | Integrate into `MainViewModel` (registration, execution) | M | #3, #4 |
| 6 | Add toolbar mic button with listening state UI | S | #5 |
| 7 | Add voice settings to `AppSettings` + Settings view | S | #5 |
| 8 | Wire up keyboard shortcut (F4) in `MainWindow` | S | #5 |
| 9 | Add toast feedback for recognition results | S | #5 |
| 10 | Update Help view, ShortcutConflictService, SHORTCUTS.md | S | #6 |
| 11 | Write unit tests for command matcher | S | #4 |
| 12 | Manual testing + tuning confidence threshold | M | All |

**S** = Small (< 2 hours), **M** = Medium (2-4 hours)

### Phase 2 Milestones

| # | Task | Dependencies |
|---|------|--------------|
| 13 | Parameterized commands (branch name, commit message) | Phase 1 |
| 14 | Continuous listening with wake phrase | Phase 1 |
| 15 | macOS support (NSSpeechRecognizer or Whisper) | Phase 1 |
| 16 | Custom voice aliases in settings UI | Phase 1 |
| 17 | Voice command discoverability in palette | Phase 1 |

### Phase 3 Milestones

| # | Task | Dependencies |
|---|------|--------------|
| 18 | Voice dictation to terminal | Phase 2 |
| 19 | Context-aware commands | Phase 2 |
| 20 | Whisper integration for offline high-accuracy | Phase 2 |

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `System.Speech` accuracy too low | Commands misfire | Constrained grammar + high confidence threshold + confirmation for destructive actions |
| Background noise triggers commands | Unintended actions | Push-to-talk default; no always-on listening in Phase 1 |
| Microphone permission denied | Feature unusable | Graceful degradation; clear error message; feature stays hidden |
| Conflicts with terminal audio | Confusing UX | Mute mic while terminal is playing audio (edge case) |
| Speech API not available on all Windows versions | Limited reach | Check availability at startup; hide feature if unavailable |
| Performance impact of speech engine | App sluggish | Lazy-load speech engine only when voice is enabled |

## Success Criteria

1. User can hold F4, say "commit", and have it execute identically to Ctrl+Shift+C
2. User can say "push" and have git push execute
3. User can say "git changes" and have the git panel open
4. Recognition latency < 1 second from end of speech to action execution
5. False positive rate < 5% (commands executing when not intended)
6. Feature has zero impact on startup time or memory when disabled

## Open Questions

1. **Shortcut choice**: F4 is available but unconventional for voice. Should we use a different key? Dedicated toolbar button may be sufficient without a shortcut.
2. **Destructive command confirmation**: Should "push" require voice confirmation ("push — say 'yes' to confirm") or execute immediately like the keyboard shortcut does?
3. **Multi-language support**: System.Speech supports multiple languages. Should we expose language selection, or default to system locale?
4. **Wake word feasibility**: Is "Hey Terminal" / "OK Host" distinctive enough to avoid false activations? May need testing.
