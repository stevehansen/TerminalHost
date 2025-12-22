# PRD: Public Website (GitHub Pages)

## Status: Implemented

GitHub Pages documentation site for TerminalHost.

## Site Structure

```
docs/
├── _config.yml          # Jekyll config (minima theme, auto dark/light)
├── _includes/nav.md     # Shared navigation
├── index.md             # Homepage
├── getting-started.md   # CLI usage, opening projects
├── usage.md             # CLI args, layout modes, config location
├── shortcuts.md         # Keyboard shortcuts reference
├── feature-tour.md      # Workflow walkthroughs
└── developer.md         # Tech stack, build commands
```

## Enable GitHub Pages

1. Repository → Settings → Pages
2. Source: Deploy from a branch
3. Branch: `master`, Folder: `/docs`

URL: `https://stevehansen.github.io/TerminalHost/`

## Future Enhancements

- Add screenshots/GIFs to homepage (`docs/assets/`)
- Add download/install instructions once releases are published
