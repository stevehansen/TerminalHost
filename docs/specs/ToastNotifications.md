# PRD: Toast Notification System

## Overview

A toast notification system for displaying transient messages to users without interrupting their workflow. Toasts appear in the bottom-right corner and persist across tab switches, providing feedback for operations like settings saves, PR checkouts, and merge operations.

## Problem Statement

Currently, several operations lack user feedback:
- **Settings save**: No indication that settings were saved successfully
- **PR Review checkout**: Status message in Dashboard sidebar is easily missed (small, dim, and user switches tabs immediately)
- **Merge operations**: Multi-step process with no progress indication
- **General operations**: Some use modal dialogs which interrupt workflow

## Goals

1. Provide non-intrusive feedback for operations
2. Support both simple notifications and progress-style updates
3. Persist visibility across tab switches
4. Be testable via service abstraction

## Features

### Toast Types

#### 1. Simple Toast
Basic notification with icon and message.

```
[icon] Settings saved successfully              [X]
```

#### 2. Progress Toast
Updateable toast for multi-step operations. Can update icon, message, and optionally show progress.

```
[spinner] Checking out PR #123...               [X]
```
Then updates to:
```
[check] PR #123 checked out                     [X]
```

### Toast Properties

| Property | Type | Description |
|----------|------|-------------|
| Id | string | Unique identifier (for updates) |
| Icon | string | Icon/emoji to display |
| Message | string | Toast message text |
| Type | enum | Info, Success, Warning, Error, Progress |
| AutoClose | bool | Whether to auto-close (default: true) |
| AutoCloseDelay | TimeSpan | Time before auto-close (default: 5s) |
| IsCloseable | bool | Show close button (default: true) |

### Behavior

1. **Positioning**: Bottom-right corner, above any status bar
2. **Stacking**: New toasts appear above existing ones
3. **Max visible**: 5 toasts maximum, additional toasts queued
4. **Queue**: When a toast closes, next queued toast appears
5. **Dismissal**: Click anywhere on toast OR click X button
6. **Animation**: Slide in from right, fade out on close
7. **Persistence**: Toasts remain visible when switching tabs

### Service Interface

```csharp
public interface IToastService
{
    /// <summary>
    /// Shows a simple toast notification.
    /// </summary>
    /// <param name="message">The message to display</param>
    /// <param name="type">Toast type (Info, Success, Warning, Error)</param>
    /// <param name="autoClose">Whether to auto-close after delay</param>
    /// <returns>Toast ID for reference</returns>
    string Show(string message, ToastType type = ToastType.Info, bool autoClose = true);

    /// <summary>
    /// Shows a toast with custom icon.
    /// </summary>
    string Show(string message, string icon, bool autoClose = true);

    /// <summary>
    /// Creates a progress toast that can be updated.
    /// </summary>
    /// <param name="message">Initial message</param>
    /// <returns>Progress toast handle for updates</returns>
    IProgressToast ShowProgress(string message);

    /// <summary>
    /// Updates an existing toast by ID.
    /// </summary>
    void Update(string id, string message, string? icon = null);

    /// <summary>
    /// Closes a specific toast by ID.
    /// </summary>
    void Close(string id);

    /// <summary>
    /// Closes all toasts.
    /// </summary>
    void CloseAll();
}

public interface IProgressToast : IDisposable
{
    string Id { get; }
    void Update(string message, string? icon = null);
    void Complete(string message, ToastType type = ToastType.Success);
    void Fail(string message);
    void Close();
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error,
    Progress
}
```

### Usage Examples

#### Simple Toast (Settings Save)
```csharp
_toastService.Show("Settings saved", ToastType.Success);
```

#### Progress Toast (PR Checkout)
```csharp
using var toast = _toastService.ShowProgress("Checking out PR #123...");

var success = await _gitHubService.CheckoutPullRequestAsync(localPath, pr.Number);

if (success)
    toast.Complete($"PR #{pr.Number} checked out");
else
    toast.Fail($"Failed to checkout PR #{pr.Number}");
```

#### Multi-Step Progress (Merge Operation)
```csharp
using var toast = _toastService.ShowProgress("Starting merge...");

toast.Update("Fetching latest changes...");
await FetchAsync();

toast.Update("Merging PR #123...");
var result = await MergeAsync();

if (result.Success)
    toast.Complete("Merge completed successfully");
else
    toast.Fail($"Merge failed: {result.Error}");
```

## UI Design

### Visual Style
- Background: Semi-transparent dark (`#E0202020`)
- Border: Subtle border matching app theme (`BorderSubtleBrush`)
- Border radius: 6px
- Min width: 250px, Max width: 400px
- Padding: 12px
- Shadow: Subtle drop shadow for elevation

### Icons by Type
| Type | Icon | Color |
|------|------|-------|
| Info | ℹ️ or (i) | SyntaxBlueBrush |
| Success | ✓ | SyntaxGreenBrush |
| Warning | ⚠ | SyntaxYellowBrush |
| Error | ✕ | SyntaxRedBrush |
| Progress | Spinner | TextSecondaryBrush |

### Layout
```
┌─────────────────────────────────────┐
│ [Icon]  Message text here      [X]  │
└─────────────────────────────────────┘
```

## Implementation Notes

### Components
1. **IToastService** - Service interface for DI
2. **ToastService** - Implementation managing toast lifecycle
3. **ToastViewModel** - Individual toast state
4. **ToastContainerView** - Host control (in MainWindow)
5. **ToastItemView** - Individual toast UI

### MainWindow Integration
Add toast container as overlay in MainWindow.xaml:
```xml
<Grid>
    <!-- Existing content -->

    <!-- Toast container (overlay, bottom-right) -->
    <views:ToastContainerView
        VerticalAlignment="Bottom"
        HorizontalAlignment="Right"
        Margin="0,0,16,16"/>
</Grid>
```

### Thread Safety
- Toast operations must be marshaled to UI thread
- Service handles dispatching internally

### Testing
- Mock `IToastService` in unit tests
- Verify toast calls without UI

## Migration Plan

### Phase 1: Core Implementation
1. Create `IToastService` interface
2. Implement `ToastService`
3. Create toast UI components
4. Register in DI container

### Phase 2: Integrate with Existing Features
1. ~~Settings save feedback~~ **DONE** - Shows success toast on save
2. ~~Dashboard PR checkout status~~ **DONE** - Progress toast for checkout and review actions
3. Merge operation progress - *Pending*

### Phase 3: Replace Modal Dialogs (Optional)
Evaluate replacing some non-critical modal dialogs with toasts.

## Implementation Status

**Completed:**
- Core IToastService interface and ToastService implementation
- ToastViewModel for individual toast state
- ToastContainerView and ToastItemView UI components
- ToastWindow - separate transparent overlay window (WPF airspace workaround)
- DI registration in App.xaml.cs
- MainWindow creates and manages ToastWindow
- Settings save feedback (SettingsTabViewModel)
- Dashboard PR checkout and review actions (DashboardTabViewModel)

**Implementation Note - WPF Airspace:**
The terminal control uses an HWND (native window) which always renders on top of WPF content in the same window. To display toasts above terminals, we use a separate transparent `ToastWindow` that:
- Is owned by MainWindow (follows minimize/restore)
- Tracks owner position and size changes
- Uses WS_EX_NOACTIVATE to avoid stealing focus
- Renders above the main window content

**Pending:**
- Merge operation progress toasts
- Additional feature integrations as needed

## Out of Scope (v1)
- Toast actions (buttons within toast)
- Toast persistence across app restarts
- Toast history/log
- Custom toast templates
- Sound notifications
