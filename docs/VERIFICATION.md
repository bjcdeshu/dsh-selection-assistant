# Verification record / 验证记录

This records observed results, not a promise of universal application compatibility.

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
