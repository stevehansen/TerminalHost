# Refactoring MainViewModel Dependencies

## Status
*   **Date:** 2025-12-26
*   **Status:** Completed
*   **Goal:** Reduce constructor over-injection in `MainViewModel` and improve maintainability by introducing a Factory pattern for child ViewModels.

## Problem
The `MainViewModel` class had over 30 dependencies in its constructor. Many of these were "Pass-Through Dependencies"—injected solely to be passed to child ViewModels (like `FileExplorerViewModel`, `DashboardTabViewModel`) and never used by `MainViewModel` itself.

## Solution: IViewModelFactory

We introduced `IViewModelFactory` to handle the creation of child ViewModels. This separates the concern of *dependency resolution* from the `MainViewModel`.

### 1. New Interface
Located in: `src/TerminalHost/TerminalHost/Services/IViewModelFactory.cs`

```csharp
public interface IViewModelFactory
{
    FileExplorerViewModel CreateFileExplorer(string rootPath);
    FileViewerViewModel CreateFileViewer(bool isDetached = false);
    DashboardTabViewModel CreateDashboard(MainViewModel parent);
    WorkspaceSidebarViewModel CreateWorkspaceSidebar();
    SettingsTabViewModel CreateSettings();
    StatisticsTabViewModel CreateStatistics();
}
```

### 2. Implementation
The `ViewModelFactory` implementation (in `Services`) holds the `IServiceProvider` and resolves dependencies directly when creating ViewModels. This means `MainViewModel` no longer needs to hold dependencies for:
*   `IGitIgnoreService`
*   `IFileExplorerService`
*   `IFileEditService`
*   `IGitHubService`
*   `IMarkdownService`
*   `IGitWorktreeService`

### 3. Impact
*   **Constructor Complexity:** Reduced by 6 parameters immediately.
*   **Testing:** `MainViewModelTests` are cleaner as they mock the factory instead of providing 6+ unused mocks.
*   **Maintainability:** Adding a dependency to `FileExplorerViewModel` no longer requires changing `MainViewModel`'s constructor.

## Verification
*   **Build:** Successful.
*   **Tests:** `MainViewModelTests` updated and passing.
