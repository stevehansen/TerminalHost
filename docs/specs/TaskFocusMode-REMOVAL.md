# Task/Focus Mode Removal Plan

> **Decision**: Remove Task Panel and Focus Mode in favor of Timeline Mode.
> Timeline Mode provides better structure (worktree-based intents) and automatic session tracking.
> ScratchPad is kept for quick unstructured notes.

## Rationale

| Feature | Task Panel | Timeline Mode |
|---------|------------|---------------|
| Tracking | Manual | Automatic (hooks) |
| Scope | Abstract (spans projects) | Concrete (1 worktree) |
| Sessions | None | Full Claude Code tracking |
| Visual | List view | Timeline view |

Timeline Mode covers the use case better with automatic tracking. Abstract tasks spanning multiple projects are rare; most work is branch-based.

## Files to Delete

### Domain Models
- [ ] `src/TerminalHost.Core/Domain/FocusTask.cs`
- [ ] `src/TerminalHost.Core/Domain/FocusTaskStatus.cs`
- [ ] `src/TerminalHost.Core/Domain/FocusModeState.cs`
- [ ] `src/TerminalHost.Core/Domain/QuickNote.cs`

### Services
- [ ] `src/TerminalHost.Core/Interfaces/ITaskService.cs`
- [ ] `src/TerminalHost.Core/Services/TaskService.cs`

### ViewModels
- [ ] `src/TerminalHost/TerminalHost/ViewModels/TaskPanelViewModel.cs`

### Views
- [ ] `src/TerminalHost/TerminalHost/Views/Popups/TaskPanelView.xaml`
- [ ] `src/TerminalHost/TerminalHost/Views/Popups/TaskPanelView.xaml.cs`
- [ ] `src/TerminalHost/TerminalHost/Views/Popups/QuickTaskView.xaml`
- [ ] `src/TerminalHost/TerminalHost/Views/Popups/QuickTaskView.xaml.cs`
- [ ] `src/TerminalHost/TerminalHost/Views/Popups/QuickNoteView.xaml`
- [ ] `src/TerminalHost/TerminalHost/Views/Popups/QuickNoteView.xaml.cs`

### Documentation
- [ ] `docs/specs/TaskFocusMode.md` (delete or archive)

## Files to Modify

### Configuration
- [ ] `src/TerminalHost.Core/Domain/AppConfiguration.cs`
  - Remove `FocusMode` property
  - Remove `Tasks` property
  - Remove `QuickNotes` property
  - Update `IsDefaultConfig()` method

### DI Registration
- [ ] `src/TerminalHost/TerminalHost/App.xaml.cs`
  - Remove `ITaskService` / `TaskService` registration
  - Remove `TaskPanelViewModel` registration

### Main Window
- [ ] `src/TerminalHost/TerminalHost/MainWindow.xaml`
  - Remove TaskPanelView from popup hosts
  - Remove QuickTaskView from popup hosts
  - Remove QuickNoteView from popup hosts

- [ ] `src/TerminalHost/TerminalHost/MainWindow.xaml.cs`
  - Remove Ctrl+T handler (OpenTaskPanel)
  - Remove Ctrl+Shift+Q handler (QuickAddTask)
  - Remove Ctrl+Shift+M handler (QuickAddNote)

### Main ViewModel
- [ ] `src/TerminalHost/TerminalHost/ViewModels/MainViewModel.cs`
  - Remove `TaskPanelRequested` event
  - Remove `TaskPanelViewModel` property/field
  - Remove `OpenTaskPanel()` method
  - Remove any focus mode filtering logic

### Shortcut Registry
- [ ] `src/TerminalHost.Core/Services/ShortcutConflictService.cs`
  - Remove `Ctrl+T` - "Open task panel"
  - Remove `Ctrl+Shift+Q` - "Quick add task"
  - Remove `Ctrl+Shift+M` - "Quick add note"

### Documentation
- [ ] `SHORTCUTS.md`
  - Remove task panel shortcuts from "Notes & Tasks" section
  - Can rename section to just "Notes" (ScratchPad only)

- [ ] `CLAUDE.md`
  - Remove any task panel references from keyboard shortcuts section

### Tests
- [ ] `tests/TerminalHost.Tests/ViewModels/MainViewModelTests.cs`
  - Remove task-related tests (if any)

## Shortcuts Freed Up

After removal, these shortcuts become available:
- `Ctrl+T` - commonly used for "new tab" in browsers, could repurpose
- `Ctrl+Shift+Q` - available
- `Ctrl+Shift+M` - available

## Migration Notes

For users with existing `config.json`:
- Old `focusMode`, `tasks`, `quickNotes` fields will be ignored
- No data migration needed (fields just won't be loaded)
- JSON deserialization handles unknown fields gracefully

## Verification Steps

1. Delete all files listed above
2. Modify all files listed above
3. Build solution: `dotnet build`
4. Run tests: `dotnet test`
5. Manual test:
   - Verify app starts without errors
   - Verify Ctrl+T does nothing (or repurpose)
   - Verify ScratchPad (Ctrl+Shift+N) still works
   - Verify Timeline Mode (Ctrl+Shift+I) works
