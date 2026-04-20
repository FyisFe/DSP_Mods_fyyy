# Changelog

## 1.0.0 — Initial release

- Search bar added to the blueprint browser; matches are searched recursively across all folders.
- Left-click a result to use the blueprint; right-click to jump to its containing folder.
- Progressive rendering: all matches are shown, streamed in batches across frames so typing stays snappy on large libraries.
- Configurable debounce delay and optional hard cap on total results (`MaxResults`, default `0` = unlimited).
