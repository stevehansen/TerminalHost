# Product Requirement: MainWindow XAML Refactor

## Overview

`MainWindow.xaml` has grown to ~140 KB with 2,200+ lines that inline templates, popups, and control logic. Although the code-behind has already been split into partial classes, the XAML is still monolithic, making it difficult to reason about, reuse components, or apply targeted changes without risking regressions. This PRD defines the steps required to break the window into modular assets so future features can be implemented faster and with less risk.

## Problem Statement

- The current XAML mixes layout composition, tab templates, popup markup, and resource definitions inside a single file.
- Designers and developers cannot safely update a section without loading the entire file, which slows Visual Studio/Blend and increases merge conflicts.
- Shared visuals (tab headers, popups) cannot be reused by other windows because they live inside `MainWindow.xaml`.
- Unit/UI tests cannot target smaller components, so every change requires full manual regression.

## Goals

1. Reduce `MainWindow.xaml` to only high-level layout composition and popup hosts.
2. Extract reusable resources and templates into dictionaries that can be merged elsewhere.
3. Introduce dedicated UserControls for the tab strip, terminal content areas, and popups.
4. Preserve existing visuals/behaviour (no redesign) while enabling isolated maintenance.

## Non-Goals

- Changing styling, colours, or UX flows.
- Rewriting the ViewModel layer or command routing.
- Introducing additional frameworks (e.g., new MVVM libraries).

## Success Metrics

| Metric | Target |
| --- | --- |
| `MainWindow.xaml` line count | < 400 lines |
| Reusable resource dictionaries created | ≥ 2 (Tab templates + tab content templates) |
| UserControls introduced | Tab strip + terminal pair view + Git/Links popups |
| Manual regression checklist completion | 100% of items executed |

## Proposed Solution & Implementation Steps

### Step 1 – Create resource dictionaries for shared templates

- Add `Resources/TabHeaderTemplates.xaml` containing the four `DataTemplate`s currently embedded in `ListBox.Resources`.
- Add `Resources/TabContentTemplates.xaml` to house the `DataTemplate`s in `ContentControl.Resources`.
- Move any inline styles (e.g., `TabCloseButton`, `TerminalSwitchButton` overrides) that are not already in `App.xaml` into these dictionaries or existing shared dictionaries.
- Merge the new dictionaries inside `App.xaml` so every view can access them (`Application.Resources.MergedDictionaries`).
- Update `MainWindow.xaml` to reference the shared resources via `StaticResource`.

### Step 2 – Introduce a `TabStrip` UserControl

- Create `Views/TabStrip.xaml(.cs)` that encapsulates the docked buttons, scroll buttons, dropdown trigger, and the `ListBox`.
- Expose dependency properties for:
  - `ItemsSource` (`IEnumerable<ITabViewModel>`)
  - `SelectedItem` (two-way binding to `SelectedTab`)
  - Commands for `OpenNewProject`, `OpenSettings`, `OpenProfiles`, `OpenStatistics`.
  - Routed events for scroll/overflow actions (or dependency properties for button visibility).
- Replace the top border in `MainWindow.xaml` with `<views:TabStrip ... />`, binding to the existing view model properties.
- Move tab drag/drop handlers into the control’s code-behind (or forward events to the window via routed events).

### Step 3 – Break out tab content controls

- Create `Views/Tabs/TerminalPairView.xaml` that contains the entire grid currently under the `TerminalPairTabViewModel` `DataTemplate`.
- Move the run-terminal splitters, quick command buttons, and terminal presenters into this control; make all bindings relative to its `DataContext` (which will be the tab view model).
- Update `Resources/TabContentTemplates.xaml` so the terminal template becomes `<views:TerminalPairView/>`.
- Keep existing `SettingsView`, `ProfilesView`, and `StatisticsView` as-is but reference them from the dictionary rather than embedding them inside `MainWindow.xaml`.

### Step 4 – Convert popups into dedicated views

- Create `Views/Popups/GitBranchPopup.xaml(.cs)` and move the large popup currently starting at line ~1,850 into that control. Surface events/commands such as `CheckoutRequested`, `DeleteRequested`, etc., or bind directly to `MainWindow` commands via `RelativeSource AncestorType=Window`.
- Reuse (or replace) the existing `Views/DetectedLinksPopup` by hosting it inside the popup element rather than duplicating markup in `MainWindow.xaml`.
- For each popup host (`Popup` element) keep only the `Placement`, `IsOpen`, and child control reference inside `MainWindow.xaml`.
- **(Done)** Extracted Scratch Pad popup into `Views/ScratchPadView.xaml` and `ViewModels/ScratchPadViewModel.cs`.
- **(Done)** Extracted Git Branch popup into `Views/Popups/GitBranchView.xaml` and `ViewModels/GitBranchViewModel.cs`.

### Step 5 – Clean up `MainWindow.xaml`

- After the extractions, ensure `MainWindow.xaml` only contains:
  - Root grid and row definitions.
  - `<views:TabStrip .../>`.
  - `<ContentControl Content="{Binding SelectedTab}" .../>` referencing the external content templates.
  - Empty-state grid.
  - Popup declarations whose content is now `<views:GitBranchPopup .../>`, `<views:DetectedLinksPopup .../>`, etc.
- Remove now-unused code-behind event hooks; rewire them either inside the new controls or via routed events.
- Update `MainWindow.xaml.cs` (and partial files) to find controls through the new UserControls (e.g., fields for `TabStrip`, `GitBranchPopup`).

### Step 6 – Validation & Regression

- Build a regression checklist that covers:
  1. Tab CRUD, drag/drop, overflow scroll, dropdown, switcher.
  2. Terminal switching, splitters, quick command buttons.
  3. Settings/Profiles/Statistics tabs loading correctly.
  4. Git branch popup interactions (search, checkout, create, delete, fetch, pull).
  5. Detected links popup detection and actions.
  6. Command palette, scratch pad, and other overlays unaffected.
- Execute the checklist after each major extraction to catch regressions early.
- Optional: add a lightweight UI test (e.g., using automated UI frameworks) or screenshot tests for the new `TabStrip`.

## Dependencies & Risks

| Item | Notes |
| --- | --- |
| Existing code-behind partials | Event handlers tied to named elements must be re-hooked once names move into UserControls. |
| Resource dictionary merge order | Ensure new dictionaries load after base styles so they can reference shared resources. |
| ViewModel assumptions | Some bindings refer to `RelativeSource AncestorType=Window`; verify they still resolve from the new controls. |

## Milestones

1. **Milestone A (Shared resources)** – Tab templates + content templates extracted, `MainWindow.xaml` updated to consume them.
2. **Milestone B (Tab strip control)** – New control in place, all tab interactions functional.
3. **Milestone C (Terminal pair control)** – Terminal layout extracted, run terminal logic verified.
4. **Milestone D (Popups modularized)** – Git/links popups hosted via dedicated views.
5. **Milestone E (Regression pass)** – Checklist executed, bugs fixed, PR ready.

## Rollout Plan

- Work behind a feature branch; no runtime flag needed because the refactor is structural.
- Merge after Regression checklist passes and peer review approvals are obtained.
- Communicate the new structure to the team (short Loom/video or README blurb) so future contributors know where templates live.

