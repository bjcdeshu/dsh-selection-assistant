# Security policy

This is an early Windows desktop plugin that deliberately handles text selections. Treat installing it as authorizing trusted host-side code, not as installing a sandboxed web extension.

Security boundaries:
- Global selection monitoring is opt-in and pausable.
- Selected content is local until an explicit translation/explanation/summary action.
- Model requests have no tools or conversation history and cannot execute commands.
- Native helpers receive OS environment variables only, not DSH/model credentials.
- IPC is bounded JSONL over inherited stdio. No HTTP listener or unauthenticated debug port.
- Clipboard fallback is opt-in. App filters and secret heuristics are not complete data-loss prevention.
- Untrusted text and model output are rendered as text, never executable HTML/JavaScript.
- No plugin-owned content history or telemetry. Host and model-provider policies still apply.

Please report vulnerabilities privately using GitHub's private vulnerability reporting when enabled, or contact the repository owner without including live keys or private selected text. Do not post exploit reports with real credentials in a public issue.

A compromised DSH host, OS account, upstream native dependency, or model provider is outside this plugin's isolation boundary. Native crash isolation improves reliability, not protection from a malicious local account.
