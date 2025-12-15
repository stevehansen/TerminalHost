## 2024-05-23 - Initial Palette Setup
**Learning:** UX improvements should be small, high-impact, and accessible.
**Action:** Starting with observation phase.

## 2024-05-24 - Accessibility Labels
**Learning:** Icon-only buttons and input fields without labels are major accessibility blockers. In XAML, `AutomationProperties.Name` is the standard fix, similar to `aria-label`.
**Action:** Always check `TextBox` and icon-only `Button` elements for `AutomationProperties.Name`.

## 2024-05-25 - XAML Accessibility Properties
**Learning:** In WPF XAML, `AutomationProperties.Name` serves the same purpose as `aria-label` in HTML for providing accessible names to controls like icon-only buttons. `AutomationProperties.HelpText` is useful for providing additional context, similar to `aria-description` or tooltips.
**Action:** When working with XAML, identify icon-only buttons (often using emoji or symbol text) and ensure they have `AutomationProperties.Name` set. Check visual indicators (like spinners) and add `AutomationProperties.HelpText` if they convey meaning.
