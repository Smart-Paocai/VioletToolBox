## Changes

Add Japanese to the existing language switcher, with persistent `ja-JP` selection and terminology aligned with the Japanese README. Translate navigation, controls, instructions, safety messages and device status text. Preserve contributor names, product names and technical identifiers.

Keep original ComboBox values for commands and ROM API lookups, so translated captions do not change EDL restart actions, MediaTek OCDT image size selection, extraction methods or ROM queries. Read flash options by control name rather than translated captions. Restore inherited fonts when switching back and retain error-message punctuation.

Add the two sidebar SVG resources already referenced by the XAML. Add a Windows submission script that builds, runs localization regression checks, commits only the listed files, pushes to the fork and creates or reuses an upstream PR.

## Validation

- Release build on Windows / .NET 8.
- WPF regression checks for language cycling, translation coverage, command identifiers, preferences and font restoration.
- No device flashing or EDL operations were performed.

Runtime logs, messages from external tools and separate tool dialogs retain their existing language behavior.
