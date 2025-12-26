# PRD: UI Standardization and Refactoring

## 1. Objective
Refactor the application's UI components to eliminate code duplication, unify user interaction patterns (popups, closing, resizing), and standardize content viewing (diffs, markdown). This will improve maintainability and ensure a consistent user experience.

## 2. Original Issues (Resolved)
*   ~~**Inconsistent Popups:** Multiple views implement their own dragging, resizing, and "close on Escape" logic.~~ → Solved with `DraggablePopup` control.
*   ~~**Fragmented Diff Viewing:** `GitFilesView` has a rich diff viewer while `PrReviewView` uses a plain `TextBox`.~~ → Solved with `DiffViewer` control.
*   ~~**Isolated Markdown Rendering:** `MarkdownPreviewWindow` had `WebView2` markdown but general previewers showed plain text.~~ → Solved with `MarkdownViewer` control.
*   ~~**Missing Standards:** Common UI elements like "Close (X)" buttons and header styles were re-implemented repeatedly.~~ → Standardized in `DraggablePopup`.

## 3. Implemented Controls

### 3.1. `DraggablePopup` Control ✅
Location: `Controls/DraggablePopup.xaml`
*   **Features:**
    *   Standard header with title area and "Close (X)" button
    *   Built-in draggability via header
    *   Built-in resize grip (bottom-right)
    *   Escape key handling to close
    *   `HeaderLeftContent` and `HeaderRightContent` slots for custom buttons
    *   `PopupContent` slot for main content
*   **Currently used by:**
    *   `GitFilesView` ✅
    *   `PrReviewView` ✅
    *   `FileViewerPopup` ✅
    *   `FilePreviewView` ✅

### 3.2. `DiffViewer` Control ✅
Location: `Controls/DiffViewer.xaml`
*   **Features:**
    *   Accepts diff patch text via `DiffText` property
    *   Renders color-coded additions/deletions using `FlowDocument`
*   **Currently used by:**
    *   `GitFilesView` ✅
    *   `PrReviewView` ✅

### 3.3. `MarkdownViewer` Control ✅
Location: `Controls/MarkdownViewer.xaml`
*   **Features:**
    *   Uses `WebView2` for rich HTML markdown rendering
    *   Accepts HTML via `HtmlContent` property
*   **Currently used by:**
    *   `FilePreviewView` ✅ (read-only preview popup)

## 4. Remaining Work

### 4.1. Known Issues
- [x] **DraggablePopup resize grip not visible**: Fixed by adding `Panel.ZIndex="100"` to ensure grip renders above content.
- [x] **DraggablePopup has no design-time preview**: Fixed by adding a design-time only Border that shows a mock window frame.

### 4.2. Pending Migrations
- [ ] `ScratchPadView` - Has custom drag header and resize grip, should migrate to `DraggablePopup`

### 4.3. Dead Code Removed
- [x] `FileEditView.xaml` and `FileEditView.xaml.cs` - **Deleted** (was orphaned)
- [x] `FileEditViewModel.cs` - **Deleted** (was orphaned)
- Note: `FileViewerPopup` provides edit mode functionality

### 4.4. Feature Gaps
- [x] **FileViewerPopup markdown support**: Added `IsMarkdownMode` and `RenderedHtml` properties to `FileViewerViewModel`. Now uses `MarkdownViewer` for `.md`/`.markdown` files in preview mode.

## 5. Success Metrics
*   ✅ Reduction in LOC due to removal of duplicate drag/resize logic
*   ✅ All migrated popups close consistently with the `Escape` key
*   ✅ `PrReviewView` displays rich, color-coded diffs
*   ✅ Markdown files in `FileViewerPopup` rendered as rich HTML
*   ⬜ All popups with resize use `DraggablePopup` (ScratchPadView pending)
