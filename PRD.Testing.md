# Product Requirements Document: Testing Strategy

## Overview

As **TerminalHost** grows in complexity with features like terminal pairing, git integration, and modular UI components, manual regression testing becomes increasingly time-consuming and error-prone. This document outlines the strategy for implementing a robust automated testing suite, covering both unit testing (logic) and UI testing (user interactions).

## Goals

1.  **Prevent Regressions:** Ensure new features or refactors (like the recent MainWindow refactor) do not break existing functionality.
2.  **Document Behavior:** Use tests as live documentation for how features (e.g., Git status parsing, Configuration loading) are expected to work.
3.  **Enable Refactoring:** Provide safety nets for future architectural changes.
4.  **Automate Quality Assurance:** Reduce reliance on manual testing for every release.

## Scope

The testing strategy covers two main areas:

1.  **Unit Tests:** fast, isolated tests for business logic, ViewModels, and Services.
2.  **UI / Integration Tests:** Slower, end-to-end tests that launch the application and verify visual/interactive behavior.

## Technology Stack

### Unit Testing
-   **Framework:** [xUnit](https://xunit.net/) (Standard, parallel execution support).
-   **Mocking:** [Moq](https://github.com/moq/moq) (For mocking services and dependencies).
    *   **Assertions:** [Shouldly](https://docs.shouldly.org/) (MIT licensed fluent assertions).
### UI Testing
-   **Framework:** [FlaUI](https://github.com/FlaUI/FlaUI) (FlaUI.UIA3).
    -   *Rationale:* FlaUI is a native .NET library that wraps UI Automation (UIA) APIs. It is lighter than Appium/WinAppDriver, doesn't require an external driver service to be running, and is easier to integrate into simple CI pipelines for WPF apps.

## Strategy & Phasing

### Phase 1: Unit Testing Infrastructure & Core Logic
**Objective:** Establish the test project structure and cover critical non-UI logic.

1.  **Project Setup:**
    *   Create `src/TerminalHost/TerminalHost.Tests/TerminalHost.Tests.csproj`. (Done)
    *   Add references to `TerminalHost`, `xunit`, `Moq`, `FluentAssertions`. (Done)
2.  **Target Areas:**
    *   **Services:** `ConfigurationService` (JSON parsing), `GitStatusService` (Regex parsing of git output), `ProjectDetectionService`.
    *   **ViewModels:** `MainViewModel` (Tab management logic), `TerminalPairTabViewModel` (Command logic), `GitBranchViewModel`.
    *   *Note:* ViewModels should be tested by mocking their dependencies (e.g., `IConfigurationService`, `IDialogService`).

### Phase 2: UI Testing Infrastructure
**Objective:** Automate "Smoke Tests" to verify the app starts and basic interactions work.

1.  **Project Setup:**
    *   Create `src/TerminalHost/TerminalHost.UITests/TerminalHost.UITests.csproj`.
    *   Add references to `FlaUI.UIA3`, `FlaUI.Core`, `xunit`.
2.  **Target Scenarios:**
    *   **Launch:** Verify app process starts and main window appears.
    *   **Tab Management:** Open a new tab, switch tabs, close a tab.
    *   **Settings:** Open settings window, toggle a checkbox, save, restart, verify persistence.
    *   **Terminal Pair:** Verify "Custom" and "Shell" panes are visible (using AutomationIDs).

### Phase 3: CI/CD Integration
**Objective:** Run tests automatically on Pull Requests.

1.  **GitHub Actions:**
    *   Create a workflow that runs `dotnet test`.
    *   *Note:* UI Tests might require a Windows runner with a desktop session. If headless CI is too complex initially, UI tests can remain local-only or scheduled on a specific VM.

## Testable Architecture Requirements

To support testing, the application code must adhere to certain patterns:

1.  **Dependency Injection (DI):**
    *   Services should be behind interfaces (e.g., `IGitStatusService` vs `GitStatusService`).
    *   ViewModels should accept these interfaces via constructors, allowing tests to inject Mocks.
    *   *Status:* Completed. Services are behind interfaces and injected via constructor in `MainViewModel`.
2.  **Automation IDs:**
    *   Key UI elements (Tabs, Buttons, Inputs) in XAML must have `AutomationProperties.AutomationId` set. This is crucial for stable UI tests (e.g., `AutomationId="NewProjectButton"`).

## Core Interfaces & Contracts

To ensure modularity and testability, the application relies on strict interfaces. Any new implementation must adhere to these contracts.

### 1. IConfigurationService
**Responsibility:** Manages application settings, persistence, and raw JSON handling.
**Key Methods:**
- `AppConfiguration Load()`: Loads settings from disk or returns defaults.
- `void Save(AppConfiguration configuration)`: Persists settings.
- `string LoadRawJson()`: Reads raw config file (for editor).
- `(bool success, string? error, string? warning) SaveRawJson(string json)`: Validates and saves raw JSON.

**Measurable Outcomes:**
- **Performance:** `Load()` must complete < 50ms on standard SSD.
- **Consistency:** Round-trip serialization (Save -> Load) must preserve all properties.
- **Robustness:** Must return valid default configuration if file is missing or corrupt.

### 2. IGitStatusService
**Responsibility:** Abstraction over git CLI operations for retrieving repository status.
**Key Methods:**
- `Task<GitStatus> GetGitStatusAsync(string workingDirectory)`
- `Task<List<GitFileStatus>> GetModifiedFilesAsync(string workingDirectory)`
- `Task<GitOperationResult> CheckoutBranchAsync(string workingDirectory, string branchName)`
- `Task<GitOperationResult> CreateBranchAsync(string workingDirectory, string branchName)`

**Measurable Outcomes:**
- **Performance:** `GetGitStatusAsync` < 200ms for average repositories (< 10k files).
- **Concurrency:** Must safely handle multiple concurrent requests for different directories.
- **Error Handling:** Must throw typed exceptions or return error results for non-git directories.

### 3. ISessionManager
**Responsibility:** Lifecycle management for terminal sessions (creation, tracking, cleanup).
**Key Methods:**
- `TerminalSession CreateSession(Profile profile)`
- `void TrackSession(TerminalSession session)`
- `void CloseSession(Guid sessionId)`
- `void CloseAllSessions()`

**Measurable Outcomes:**
- **Leak Prevention:** `CloseSession` must dispose all associated resources (Process, Pipes).
- **Event Timing:** `SessionCreated` and `SessionClosed` events must fire exactly once per lifecycle.

### 4. ITerminalControlFactory
**Responsibility:** Factory pattern for creating UI terminal controls.
**Key Methods:**
- `EasyTerminalControl CreateTerminalControl(TerminalSession session)`

**Measurable Outcomes:**
- **Isolation:** Created controls must be fully initialized and not depend on global state.
- **Configuration:** Returned control must have Font, Theme, and KeyBindings applied per settings.

## Implementation Plan (Todo)

- [x] **Infrastructure**
    - [x] Create `TerminalHost.Tests` project.
    - [ ] Create `TerminalHost.UITests` project.
- [x] **Refactoring for Testability**
    - [x] Extract `IConfigurationService` interface.
    - [x] Extract `IGitStatusService` interface.
    - [x] Update `MainViewModel` to accept dependencies.
- [ ] **Unit Tests Implementation**
    - [ ] Test `GitStatusService.ParseStatus` (Pure logic, high value).
    - [ ] Test `ProjectDetectionService` (File pattern matching).
    - [ ] Test `MainViewModel.AddNewTab`.
- [ ] **UI Tests Implementation**
    - [ ] Add `AutomationId`s to `MainWindow.xaml` and `TabStrip.xaml`.
    - [ ] Write `SmokeTest_AppLaunches`.
    - [ ] Write `SmokeTest_CanOpenSettings`.

## Success Metrics

-   **Coverage:** > 80% Code Coverage on *Domain* and *Services* namespaces.
-   **Reliability:** UI Smoke tests pass 100% of the time on local dev machines.
-   **Speed:** Unit test suite runs in < 5 seconds.