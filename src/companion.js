import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { attachFrames, sendFrame } from './protocol.js';
import { validateSettings } from './settings.js';
import { normalizeSelection, SelectionState } from './selection.js';
import { ACTIONS } from './prompts.js';
import { helperEnvironment } from './environment.js';

const require = createRequire(import.meta.url);
const selections = new SelectionState();
let settings, ui, hook, showTimer, stopping = false;
const active = new Map();
const host = message => sendFrame(process.stdout, message);
const surface = message => sendFrame(ui?.stdin, message);
function hide() { clearTimeout(showTimer); selections.clear(); surface({ type: 'hide-toolbar' }); }
function stopHook() { if (hook) { try { hook.stop(); hook.cleanup(); } catch {} hook = null; } }
function configure() {
  hide(); stopHook();
  if (!settings.enabled) return;
  try {
    const SelectionHook = require('selection-hook');
    hook = new SelectionHook();
    hook.on('error', () => surface({ type: 'notice', message: '取词组件暂不可用，请暂停后重启监听。' }));
    hook.on('text-selection', data => { if (settings.trigger === 'auto') selected(data); });
    hook.on('mouse-down', data => surface({ type: 'pointer', x: data.x, y: data.y, pendingId: selections.current?.id }));
    hook.on('mouse-wheel', () => hide());
    hook.on('key-down', data => {
      // The native hotkey and hook callbacks arrive on independent channels.
      // Never let the shortcut's own D key erase the selection it just created.
      if (data.sys && data.vkCode === 0x44) return;
      if (!['Shift', 'Control', 'Alt', 'Meta'].includes(data.uniKey)) hide();
    });
    if (!hook.start({ debug: false, enableClipboard: settings.clipboardFallback, enableMouseMoveEvent: false, selectionPassiveMode: settings.trigger === 'hotkey', globalFilterMode: SelectionHook.FilterMode.EXCLUDE_LIST, globalFilterList: [...settings.blockedApps, 'SelectionAssistant.UI.exe'] })) throw new Error('hook failed');
  } catch {
    stopHook(); surface({ type: 'notice', message: '原生取词组件加载失败。请确认安装了 selection-hook 的 Windows 预编译文件。' });
    host({ type: 'fatal', code: 'hook-load-failed' });
  }
}
function selected(data, options) {
  const selection = normalizeSelection(data, settings, options);
  if (!selection) { hide(); return; }
  const accepted = selections.accept(selection, settings.toolbarTimeoutMs + settings.toolbarDelayMs);
  if (!accepted) return;
  clearTimeout(showTimer);
  showTimer = setTimeout(() => surface({ type: 'selection', id: accepted.id, x: accepted.x, y: accepted.y, expiresMs: settings.toolbarTimeoutMs }), settings.toolbarDelayMs);
}
function begin(request, target) {
  const previous = active.get(target);
  if (previous) surface({ type: 'failed', id: previous, surface: target, message: '已由新请求替换' });
  active.set(target, request.id);
  surface({ type: 'begin', ...request, surface: target });
  host({ type: 'run', ...request, surface: target });
}
function startUi(executable) {
  ui = spawn(executable, [], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env: helperEnvironment() });
  ui.stdin.on('error', () => {});
  ui.stderr.on('data', () => {});
  ui.once('error', () => { host({ type: 'fatal', code: 'native-ui-start' }); shutdown(); });
  ui.once('exit', () => shutdown());
  attachFrames(ui.stdout, message => {
    if (message.type === 'ready') surface({ type: 'settings', settings });
    else if (message.type === 'action' && ACTIONS.includes(message.action)) {
      const selection = selections.take(message.id);
      if (!selection) return;
      clearTimeout(showTimer);
      begin({ ...selection, source: 'selection', action: message.action }, 'selection');
    } else if (message.type === 'submit' && ['selection', 'quick'].includes(message.surface)) {
      if (typeof message.id !== 'string' || !/^[a-zA-Z0-9-]{1,64}$/.test(message.id)) return;
      if (!ACTIONS.includes(message.action) || typeof message.text !== 'string' || !message.text.trim() || message.text.length > settings.maxChars || /\x00/.test(message.text)) {
        surface({ type: 'input-error', id: message.id, surface: message.surface, message: '请输入文字，最多 ' + settings.maxChars + ' 字。' }); return;
      }
      if (message.targetLanguage !== undefined && (typeof message.targetLanguage !== 'string' || !message.targetLanguage.trim() || message.targetLanguage.length > 160 || /[\r\n\x00]/.test(message.targetLanguage))) return;
      begin({ id: message.id, source: 'manual', action: message.action, text: message.text.trim(), ...(message.targetLanguage ? { targetLanguage: message.targetLanguage } : {}) }, message.surface);
    } else if (message.type === 'dismiss') { if (selections.current?.id === message.id) { clearTimeout(showTimer); selections.clear(); } }
    else if (message.type === 'cancel') {
      for (const [target, id] of active) if (message.id === id) { host({ type: 'cancel', id }); active.delete(target); }
    }
    else if (message.type === 'hotkey' && settings.enabled) {
      try { selected(hook?.getCurrentSelection(), { trigger: 'hotkey', cursor: message }); } catch { surface({ type: 'notice', message: '未能读取当前选区。' }); }
    } else if (message.type === 'settings-get') host({ type: 'settings-get' });
    else if (message.type === 'settings-save') host({ type: 'settings-save', settings: message.settings });
    else if (message.type === 'shutdown') shutdown();
  }, () => shutdown());
}
async function shutdown() {
  if (stopping) return; stopping = true;
  clearTimeout(showTimer); stopHook(); host({ type: 'cancel' }); active.clear();
  surface({ type: 'shutdown' });
  const timer = setTimeout(() => { ui?.kill(); process.exit(0); }, 1200);
  if (!ui || ui.exitCode !== null) { clearTimeout(timer); process.exit(0); }
  else ui.once('exit', () => { clearTimeout(timer); process.exit(0); });
}
attachFrames(process.stdin, message => {
  if (message.type === 'init' && !settings) {
    settings = validateSettings(message.settings); startUi(message.nativeExe); configure();
  } else if (message.type === 'settings') {
    const previous = settings;
    settings = validateSettings(message.settings);
    if (['enabled', 'trigger', 'clipboardFallback', 'blockedApps'].some(key => JSON.stringify(previous[key]) !== JSON.stringify(settings[key]))) configure();
    surface(message);
  } else if (message.type === 'shutdown') shutdown();
  else if (message.type === 'notice') surface(message);
  else {
    for (const [target, id] of active) if (message.id === id) {
      surface({ ...message, surface: target });
      if (message.type === 'done' || message.type === 'failed') active.delete(target);
      break;
    }
  }
}, () => shutdown());
process.stdin.on('end', shutdown);
process.stdout.on('error', shutdown);
process.on('SIGTERM', shutdown);
host({ type: 'ready' });
