# Changelog

## Unreleased — UI and quick translation

- Anchor the toolbar to the left/below the selection release point, with its visible right edge aligned to the pointer and work-area edge avoidance.
- Reuse the running DSH host's multi-resolution icon for translation/settings windows and the tray.

- Raise result windows on explicit actions, with persistent pin behavior and compact vector controls.
- Keep text outside shadow-effect buffers, round layout to display pixels and capture previews at the actual monitor DPI.
- Add a per-window 20–100% opacity slider and reliable native edge/corner resizing with minimum-size limits.
- Persist each translation window's dimensions and opacity across DSH restarts in a separate atomic preference file; restore in DIPs and fit the current monitor work area.
- Offer 96 target languages plus custom values in one searchable picker shared by settings and both translation windows. Search Chinese, English and native names without issuing a model request.
- Keep the result window's language label aligned with the global target on a fresh selection, while manual retries use the window's chosen language.

- Compact toolbar, separate editable quick translation window, consistent light/dark/system appearance, accessible controls and keyboard navigation.
- Tray and configurable shortcut (default Ctrl+Alt+T); manual translation remains available while selection monitoring is paused. Clipboard is read only on an explicit paste.
- Independent per-window requests, cancellation, retry and late-response protection, including closing before a submitted request starts.
- Mouse release anchoring with position-confidence checks; monitor work-area placement in physical pixels; per-monitor WPF scaling configuration; preserve dragged/pinned result positions and z-order.
- Reuse the streaming document at 100ms intervals and format Markdown on completion; avoid rebuilding completed documents on close.
- Build/cache all native source files and runtime config. Add repeatable renderer benchmark, native placement/theme/quick-input smoke coverage and protocol regressions.

This is local development work, not a new published release. See `docs/VERIFICATION.md` for observed results and remaining limits.

## v0.1.0 — Preview

First public MIT release for Windows + DeepSeek Harness Desktop 0.2.0-rc.2.

### Features
- Global selection using pinned MIT `selection-hook` 2.1.1, isolated from the DSH Host.
- Native WPF toolbar and streaming result window: translate, explain, summarize, copy, stop, pin, drag and resize.
- Direct, tool-free DSH LLM calls without creating Agent chat sessions.
- Tray settings, editable original prompts, language/model controls, explicit monitoring consent, app blocklist and optional clipboard fallback.
- `Ctrl+Alt+D` fallback, no new HTTP listener, no extra Chromium renderer, no helper credentials.

### Verification
- 16 automated tests and Windows/Linux CI.
- Native build, synthetic UI smoke, late-response cancellation and native-close/reopen regressions.
- Live Desktop installation without restarting or patching DSH.
- Three live model operations completed; automatic selection and displayed translation confirmed by the user.

### Known limits
- Windows only; exact host version currently pinned.
- Clipboard fallback is off by default; inaccessible, elevated, image-only and application-specific controls can fail to provide text.
- Lightweight Markdown, not browser-grade layout.
- WPF + isolated Node are not a tiny single-process utility. See `docs/VERIFICATION.md` for measured working-set/private-memory values and the limitations of those measurements.
- Distribution is a development preview, not a universal application-compatibility guarantee.
