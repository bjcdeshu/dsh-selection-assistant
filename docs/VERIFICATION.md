# Verification record / 验证记录

This records observed results, not a promise of universal application compatibility.

## 2026-10-17 follow-up — Harness 0.2.1 compatibility

- Plugin 0.1.1 declares `engines.dsh: >=0.2.0-rc.2 <0.3.0-0` and the matching optional LLM peer range. The previous exact `0.2.0-rc.2` peer rejected Desktop 0.2.1-rc.2. Same-series updates now pass without compatibility exemptions; 0.3 prereleases and older unsupported versions remain outside the range.
- The installed Desktop 0.2.1-rc.2 runtime's actual `evaluatePluginCompatibility` reproduced the old rejection and accepted the updated manifest. Nine checks covered the old manifest plus old/current/future 0.2 versions and excluded versions.
- The installed runtime's actual `LlmRuntime` with a local synthetic adapter passed model listing and all three plugin actions (translate, explain, summarize), including request controls and streamed completion. No provider request or credentials were used. All **32 JS tests** pass. Native code is unchanged.
- The separate `deployment:persona-suffix` / `{{cwd}}` error was explicitly left to the user's other agent; no Harness profile or prompt configuration was changed in this task.

## 2026-10-15 follow-up — automatic theme and selection responsiveness

The native build is `db14fa67df771cae`. The user reported that a window kept its night theme until Settings was opened, and that selecting text sometimes needed several attempts (probably in a browser).

- The previous theme listener used the message-only hotkey window, which cannot receive Windows broadcasts. Theme updates now use `SystemEvents`, marshal onto the UI dispatcher and coalesce notifications. Resume/unlock, window opening/activation and a 30-second fallback also check the current preference. Explicit light/dark choices remain fixed; unchanged colors do not repaint. See [Microsoft's message-only window explanation](https://devblogs.microsoft.com/oldnewthing/20171218-00/?p=97595) and [SystemEvents lifetime requirements](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents.userpreferencechanged).
- **103 theme assertions** pass. Six real `WM_SETTINGCHANGE` messages were sent only to the fixture's own .NET event window. The production watcher updated existing result/quick windows and sealed templates without opening Settings, recreating windows or changing text/size/opacity. Fixed themes, missed-notification fallback, dispatcher delivery and disposal were checked. Tests used an injected theme reader and non-activating offscreen windows; they did not change the user's Windows theme or registry.
- **23 popup keyboard checks** pass. Escape dismisses opacity/language flyouts first, including when focus remains in the owner; translation continues and the window remains open. Without a flyout, existing stop/close behavior is retained. The old opacity path fails the regression.
- Selection deduplication now applies only to a live toolbar, so quickly reselecting the same text after dismissal/consumption is accepted. A selection arriving before native UI startup waits for settings and shows only the latest retained snapshot. Delayed display checks the current ID and starts its expiry when shown. The six added regressions fail against the prior source and pass after these fixes.
- **32 JS tests**, native compilation and the synthetic UI smoke pass. The smoke covers manual input, pause, both themes, pinning, close/reopen, cancellation/late frames and nine toolbar placements.
- Browser reliability is not claimed to be universally solved: `selection-hook` 2.1.1 can drop mouse events while its automatic UIA retrieval is busy, and its public API has no asynchronous retry/delay option. This pass fixes confirmed application-level suppression/startup races without adding synchronous automatic reads or clipboard simulation. Real browser selection under the new code remains to be confirmed.
- The running DSH plugin has not yet been reloaded: desktop automation was stopped by physical Escape during the reload workflow. Reload the plugin through its off/on switch (Host JS is unchanged). User settings and the saved window-state file were not modified.

## 2026-10-13 follow-up — toolbar placement and host icon

The native build is `26a745c629b497c2`. The user's photos showed a preference for Cherry's toolbar extending to the left of the release point and identified the generic Windows icon used by our translation windows.

- The toolbar now has a dedicated positioning path: its visible right edge aligns with the release point, its body extends left, and its visible top sits 8 DIPs below. At a work-area edge it clamps within the screen; near the bottom it flips above. Its compact 184×48 DIP window size is retained. Result/quick-window placement and saved appearance are unchanged.
- **155 toolbar checks** pass: negative-coordinate work areas and 100/125/150/200% calculations, plus nine actual HWND placements and visual-border alignment on the current **120 DPI / 125%** display. Placement retains dimensions, topmost status and foreground focus. The full synthetic UI smoke also confirms the new release-point alignment.
- The Host sends its actual executable path through the companion. Native windows and the tray read the executable's full icon resource group in memory. **19 icon checks** pass: the installed DSH icon contains **16/24/32/48/64/128/256 px** images; the actual WPF HWND's large and small icons have **zero pixel difference** from Windows loading the same sizes from the host. No icon asset is downloaded or redistributed.
- **26 JS tests**, native compilation and synthetic UI smoke pass. DSH was normally restarted after confirming its current task was stopped. The live helper reports **ready**, both shortcuts registered, and **hostIcon: true**, with the correct host executable argument.
- The user's existing size/opacity preferences were retained. This pass does not change target-language or model preferences.

Development now proceeds directly on `main` at the user's request; the previous development branch is consolidated into the main branch after verification.

## 2026-10-13 follow-up — persistent window preferences and languages

The final native build is `4d6d510da42affe7`. This pass completes restart persistence and expands the language controls requested after the display/resize work below.

- Each translation window saves its width/height in DIPs and opacity independently in `window-state.json`, next to the active profile's `settings.json`. The Host passes the absolute path through the companion; it is independent of working directory and native build hash. Changes are debounced and atomically replaced, with close/exit/session-ending flushes. Minimized sizes are excluded and restored sizes fit the monitor work area subject to minimum dimensions.
- **30 persistence checks** pass, covering independent process reads/writes, different cache paths, corrupt/oversized files, finite numeric bounds, failure/retry, disposal and disabled persistence in previews. **53 native integration assertions** pass across two separate UI processes, including changed executable/cache/cwd, per-window state, minimizing/closing, no saved source/output text, and language request/label alignment.
- **96 target-language options**, plus automatic and custom values, are shared by settings, selection results and quick translation. Search accepts Chinese, English and native names. **236 language checks** pass, including actual WPF popup/filter/click/key behavior and legacy/custom values. These tests establish UI/request support, not equal model accuracy in every language.
- **26 JS tests**, native compilation, the full synthetic UI smoke/placement suite and **69 native resize checks** pass. Fresh selection results reflect the configured target, while manual retries retain the chosen language. Synthetic screenshots in `docs/images` were refreshed from the final build.
- Live DSH: searched `français`, selected French and submitted the synthetic sentence “The cat is sleeping on the sofa.” The configured model returned “Le chat dort sur le canapé.” without changing the global language/model settings.
- Live restart: dragged the quick window to **700 × 470.4 DIP**, set opacity to **65%**, confirmed automatic persistence, then normally exited and relaunched DSH. The new helper (also a different build cache) reopened at the same size and **65%**, with empty input/output. Both shortcuts registered successfully. The temporary test opacity was restored to **100%** afterward; dimensions remain saved.

The display/resize notes below describe the previous build. Their statement that opacity reset on restart is superseded by this pass. Mixed-DPI hardware and language-by-language model quality remain outside these checks.

## 2026-10-13 follow-up — clarity, opacity and resizing

The user found the revised layout broadly acceptable, then reported soft text and unusable resizing and requested Cherry-like opacity control. The current native build is `1a1c4b3b5b50e54e`, loaded in the existing DSH plugin through its toggle.

- The compact icon toolbar, 32–34 DIP title bar, neutral surfaces and language/result/footer arrangement follow Cherry's [toolbar](https://github.com/CherryHQ/cherry-studio/blob/main/src/renderer/components/selection/SelectionToolbarView.tsx) and [action window](https://github.com/CherryHQ/cherry-studio/blob/main/src/renderer/windows/selection/action/ActionWindow.tsx) structure. Controls are independently drawn native vectors. Explicit result actions now raise and activate once; pin controls continued topmost behavior. Streaming does not keep raising the window.
- Removed `DropShadowEffect` from ancestors of text. The shadow is an empty sibling silhouette behind an opaque content surface. Root layout rounding, pixel snapping and display text metrics are enabled. ClearType hints are used only for fully opaque content; partial opacity uses automatic rendering. This addresses a known WPF intermediate-buffer blur source, not a claim that every monitor renders identically. See [Microsoft's rendering explanation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.renderoptions.cleartypehint).
- The title-bar droplet opens a continuous 20–100% opacity slider with a reset. Each window remembers its own value while this helper process runs; closing/reopening retains it, restarting the helper resets it. It changes only local display and does not send content or change model settings.
- All four edges and corners now use Windows nonclient hit testing and native sizing cursors. A minimally nontransparent shadow gutter makes it mouse-accessible. Minimum sizes keep controls usable; a user-resized result retains its dimensions on later requests.
- **24 JS tests**, **69 native resize checks** and **10 native stacking checks** pass. Resize tests include actual HWND hit testing at **120 DPI / 125%**, minimum sizes and resize notifications, plus coordinate calculations at 100/125/150/200% and negative positions. These simulated scales are not mixed-monitor hardware tests.
- Native UI smoke passes, including opacity bounds, per-window independence, retained opacity across close/reopen, minimize/restore position, late responses, cancellation and nine monitor placements.
- Live GUI: dragging the actual quick window's bottom-right corner changed it from **780×530 to 700×470 DIP** with the content reflowing; the real slider changed to **65%** and back to **100%**. The current helper's startup status is ready with both shortcuts registered. No new model request was needed for these display checks.
- Preview capture now renders at the actual monitor DPI (e.g. 650×525 physical pixels for a 520×420 DIP result at 125%), rather than rerendering at 96 DPI. The files in `docs/images` are synthetic and regenerated from this build. A scaled preview viewer can still soften an image; assess at its original pixel size.

The resource numbers below were measured before this follow-up. They are not fresh performance measurements of the current shadow/rendering implementation.

## 2026-10-13 local development — UI / quick translation

UI refresh, selection positioning corrections and a separate manual translation window, developed on `feat/ui-v2-quick-translate` from `ef62143`. This work is not yet a published release.

### Verified in this development pass

- 24 automated tests pass: the previous 16 plus confidence-aware mouse/hotkey positioning, legacy settings migration, manual translation while paused, input validation, independent per-window requests/cancellation and stream ownership. Appearance changes do not restart the selection hook.
- Native compilation includes all C# sources, manifest and `.exe.config`; the same inputs are fingerprinted. The runtime config opts WPF into per-monitor scaling on .NET Framework 4.8.
- Native UI smoke verifies light/dark output, paused manual input, no request before Submit, quick-window content retention, close-before-begin protection, stop/late-frame rejection, close/reopen, pin-position retention and respecting unpinned z-order.
- Nine synthetic placements on the actual monitor's work area pass containment and proximity checks: **2560×1440 display, 125% scaling / 120 DPI**, work area x=70, y=0, width=2490, height=1440. These are genuine HWND rectangle checks, with synthetic anchors.
- Live Notepad test with synthetic text: a forward mouse selection displayed the new toolbar immediately beside the release point. After moving the Notepad window, a reverse selection displayed it at the opposite end. Cherry's existing toolbar was also visible on the first gesture; no Cherry settings were changed.
- Live DSH quick translation: Ctrl+Alt+T opened the window; typing a fixed English sentence did not submit it; Ctrl+Enter used the saved DSH model and displayed a completed Chinese translation. Repeated successfully with global selection monitoring paused. The original enabled/auto/clipboard-fallback-off preferences were restored afterward.
- The final live helper uses native cache hash `3c07cf9a261e9f2e`. Both shortcut registrations are confirmed by the text-free startup status. Synthetic screenshots in `docs/images` were regenerated from the final source.
- DSH 0.2.0-rc.2 keeps Host JavaScript modules cached across plugin toggles. This was confirmed by a newly created cache directory using the **old build algorithm**, followed by native compilation failure after the source split. A normal DSH exit/reopen loaded the current Host/build modules. Later native-only changes were applied by toggling this plugin; old helper processes exited.

### Measurements

The reproducible UI-only recipe in `scripts/benchmark-native.mjs` launches one native helper, idles, opens a result, streams 150 fixed chunks at 30ms intervals, and performs ten close/reopen cycles. No model calls, hook, forced GC or working-set trimming. One before/after run, with unchanged synthetic content:

| UI-only stage | Before working set / private MiB | After working set / private MiB |
|---|---:|---:|
| Idle | 72.67 / 68.45 | 77.54 / 70.25 |
| Opened | 97.05 / 83.05 | 102.57 / 84.14 |
| Streamed | 115.35 / 104.00 | 113.95 / 98.90 |
| After 10 close/reopen cycles | 120.07 / 108.54 | 121.33 / 107.12 |

CPU time between opened and streamed: 0.8125s before, 0.5469s after. This supports reduced renderer work in this recipe; memory changes are small and mixed, not a broad RAM-saving claim. The final taskbar/accessibility, button styling and stale input-error handling changes were made after this measurement; they do not change the streaming renderer.

Live two-helper snapshot after the Notepad test, over an idle five-second interval:

| Process | Working set MiB | Private MiB | CPU delta |
|---|---:|---:|---:|
| Native UI | 87.02 | 73.43 | 0.0000s |
| Node companion | 115.36 | 25.26 | 0.0000s |
| Sum | 202.38 | 98.69 | 0.0000s |

Working sets include shared pages. This live snapshot is at a different lifecycle stage from the UI-only benchmark; the two are not comparable baselines.

### Remaining coverage

- Mixed-DPI multiple monitors, other scaling factors and negative-coordinate monitors are covered only by coordinate logic, not real display hardware.
- Broad Office/PDF/elevated-app compatibility, prolonged 1/5/10-minute idle and long-duration memory growth, and a controlled Cherry resource comparison remain unmeasured.
- Real clipboard text/image contention paths and real concurrent model requests were not exercised; manual typing and pause were exercised live, request concurrency and input limits were tested with mocks.
- The UI direction uses Cherry's compact toolbar/function-window behavior as a reference, with independent native implementation. This pass is not a claim of pixel identity or user acceptance of the final appearance. Reference: [Cherry's official selection assistant documentation](https://github.com/CherryHQ/cherry-studio-docs/blob/main/cherrystudio/preview/selection-assistant.md).

The records below describe the earlier v0.1.0 baseline and remain historical evidence.

## Environment

- Windows desktop with the system .NET Framework 4.x compiler and WPF.
- DeepSeek Harness Desktop **0.2.0-rc.2**.
- `selection-hook` **2.1.1**, pinned.
- Repository CI: Node 24 on Windows and Linux.

## Completed

- 16 automated tests: safe defaults, bounded IPC, settings validation, serialized settings writes, prompt/data separation, blocked apps, invalid-coordinate handling, cancellation, stale selection dismissal, credential-free helper environment, LLM terminal failures, no tools/history, and the delayed model-catalog/settings race.
- JavaScript syntax/package checks and the current DSH array-of-insert overlay format.
- Native WPF compilation using Windows' Framework compiler; no SDK or runtime download.
- Loading the native selection dependency in **both bundled Node and DSH's packaged Electron Node mode**, without enabling monitoring for that load test.
- UI-only synthetic smoke: toolbar, result and settings render; stop rejects already-in-flight late frames; native close/Alt+F4-equivalent lifecycle permits reopening. No model, clipboard or selection hook used in that test.
- Live installation into the existing Desktop profile, with no DSH restart and no installation archive modification.
- User-enabled monitoring with clipboard fallback **off**.
- Live native UI Automation retrieval from an owned, non-sensitive text fixture.
- Live shortcut → toolbar → translation / explanation / summarization requests through the configured DSH model; all three reached the successful completion state. No full Agent sessions were created.
- **User-confirmed acceptance:** automatic mouse selection shows the toolbar, and clicking Translate displays a correct translation in the user's normal interface. This is a human confirmation, not a claim that every application was automatically exercised.

The unattended desktop mouse-fixture test was interrupted by foreground/occlusion checks and is **not** counted as a pass. The live result's text could not be extracted reliably through UI Automation; completion was observed in the plugin state and the displayed translation was subsequently confirmed by the user. These are distinct forms of evidence.

The live Windows automation harness and any local paths/provider preferences are kept in gitignored `.local/`, not distributed. Public screenshots contain only synthetic demonstration text.

## Resource snapshot (not a benchmark)

After enabling and exercising the helper UI, a five-second sample on the development machine observed:

| Added process | Working set | Private bytes | CPU time delta over ~5.01s |
|---|---:|---:|---:|
| Native WPF UI | 124.9 MiB | 119.9 MiB | 0.0156 s |
| Isolated Node-mode selection companion | 121.3 MiB | 22.4 MiB | 0.0156 s |
| Sum | 246.2 MiB | 142.3 MiB | 0.0312 s |

Working sets include shared pages, so their sum is **not** the incremental physical-memory cost. Private bytes are committed private memory, not necessarily resident RAM. This is not a controlled Cherry Studio comparison, and it does not establish a universal savings ratio. This preview is not a single-digit-MiB native utility; it trades some memory for crash isolation and native rich-text presentation. There are no additional Chromium/WebView2 rendering processes.

## Not yet established

- Broad Chrome/Edge, Office/WPS, PDF, terminal and elevated-window compatibility.
- Mixed-DPI multi-monitor stress, high-frequency repeated selections, prolonged idle/memory behavior.
- Every provider and model; custom routes remain subject to the host adapter's capabilities.
- GitHub-only clean-machine installation on machines missing .NET Framework components.
- A controlled before/after comparison with Cherry Studio using identical content and settings.

Keep these limitations visible when reporting results. If a source application does not expose accessible selected text, prefer explicit shortcut/clipboard compatibility options rather than claiming all desktop text is readable.
