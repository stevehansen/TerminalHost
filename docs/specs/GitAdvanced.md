# Advanced Git Features

TerminalHost provides a comprehensive suite of visual git tools to manage your workflow without leaving the application.

## Implemented Features

- **Commit History Viewer (Ctrl+H)**: Browse past commits with detailed metadata, file changes, and diffs. Supports copy hash, filter by author/message, and graph visualization.
- **Interactive Staging**: Stage/unstage changes at the file level in the Git Changes panel (Ctrl+G).
- **Commit Creation**: Create commits with multi-line messages, character count warnings, and support for conventional commit prefixes.
- **Stash Management (Ctrl+Shift+S)**: View, apply, pop, drop, and create stashes. Preview stash contents and create branches from stashes.
- **File History & Blame (Ctrl+Shift+B)**: Line-by-line annotations showing who changed what and when. View full history for any file.
- **Reflog Access (Ctrl+Shift+G)**: Recover "lost" commits after reset or rebase. Checkout or create branches from reflog entries.
- **Cherry-pick & Revert**: Apply specific commits to your current branch or create revert commits via the Commit History context menu.

## Planned Features

### 1. Merge Conflict Resolution (Low Priority)
A visual three-way merge interface for resolving conflicts during merge, rebase, or cherry-pick.
- **Features**: Side-by-side view (Ours/Result/Theirs), quick actions (Accept Ours/Theirs/Both), manual editing.
- **Status**: Planned.

### 2. Submodule Support (Low Priority)
Basic management of git submodules.
- **Features**: Indicator in file explorer, status display, init/update actions.
- **Status**: Planned.

### 3. Commit/Branch Comparison (Medium Priority)
Compare changes between any two refs (commits, branches, tags).
- **Features**: Summary of changes (+N/-M), file list with diffs.
- **Status**: Planned.

### 4. Tags Management (Low Priority)
View and manage git tags.
- **Features**: List all tags, create lightweight/annotated tags, push to remote.
- **Status**: Planned.

## Implementation Priority

| Priority | Feature | Status |
|----------|---------|--------|
| **Medium** | Commit/Branch Comparison | Planned |
| **Low** | Tags Management | Planned |
| **Low** | Submodule Support | Planned |
| **Low** | Merge Conflict Resolution | Planned |