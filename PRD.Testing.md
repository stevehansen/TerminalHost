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
    *   Create `src/TerminalHost/TerminalHost.Tests/TerminalHost.Tests.csproj`.
    *   Add references to `TerminalHost`, `xunit`, `Moq`, `FluentAssertions`.
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
    *   *Current State:* The app uses some static services or direct instantiation. These need to be refactored to interfaces + DI (or at least a testable Service Locator).
2.  **Automation IDs:**
    *   Key UI elements (Tabs, Buttons, Inputs) in XAML must have `AutomationProperties.AutomationId` set. This is crucial for stable UI tests (e.g., `AutomationId="NewProjectButton"`).

## Implementation Plan (Todo)

- [ ] **Infrastructure**
    - [ ] Create `TerminalHost.Tests` project.
    - [ ] Create `TerminalHost.UITests` project.
- [ ] **Refactoring for Testability**
    - [ ] Extract `IConfigurationService` interface.
    - [ ] Extract `IGitStatusService` interface.
    - [ ] Update `MainViewModel` to accept dependencies.
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
