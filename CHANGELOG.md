# Changelog

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
