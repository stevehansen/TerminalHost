## 2024-05-23 - Initial Palette Setup
**Learning:** UX improvements should be small, high-impact, and accessible.
**Action:** Starting with observation phase.

## 2024-05-24 - Accessibility Labels
**Learning:** Icon-only buttons and input fields without labels are major accessibility blockers. In XAML, `AutomationProperties.Name` is the standard fix, similar to `aria-label`.
**Action:** Always check `TextBox` and icon-only `Button` elements for `AutomationProperties.Name`.
