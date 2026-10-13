import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import { join, resolve } from 'node:path';
import { once } from 'node:events';
import { buildNative } from './build-native.mjs';
import { attachFrames, sendFrame } from '../src/protocol.js';
import { helperEnvironment } from '../src/environment.js';
import { validateSettings } from '../src/settings.js';

// UI-only smoke test: no selection-hook, no global hotkey, no clipboard, no model.
const output = resolve('.local', 'previews-' + Date.now());
await mkdir(output, { recursive: true });
const executable = await buildNative();
const child = spawn(executable, ['--demo', output], { windowsHide: true, stdio: ['pipe', 'pipe', 'inherit'], env: helperEnvironment() });
const waiting = new Map();
const seen = new Map();
const send = message => sendFrame(child.stdin, message);
child.stdin.on('error', () => {});
attachFrames(child.stdout, message => {
  if (message.type === 'demo-error') { console.error(message.message); child.kill(); return; }
  const key = message.type === 'captured' ? 'captured:' + (message.name || message.surface) : message.type === 'demo-state' ? 'state:' + message.nonce : message.type;
  seen.set(key, message); waiting.get(key)?.(message); waiting.delete(key);
}, error => { console.error(error.message); child.kill(); });
const closed = new Promise((_, reject) => child.once('exit', code => reject(new Error('Native UI exited early (' + code + ')'))));
closed.catch(() => {});
const wait = key => Promise.race([closed, seen.has(key) ? Promise.resolve(seen.get(key)) : new Promise(resolve => waiting.set(key, resolve))]);
const timeout = setTimeout(() => { console.error('Native UI smoke timed out'); child.kill(); process.exitCode = 1; }, 30000);
let nonce = 0;
const state = async (surface = 'selection') => { const id = String(++nonce); send({ type: 'demo-state', surface, nonce: id }); return wait('state:' + id); };
const capture = async (surface, name = surface) => { send({ type: 'capture', surface, name }); return wait('captured:' + name); };
try {
  await wait('ready');
  send({ type: 'settings', settings: validateSettings({ enabled: true }) });
  send({ type: 'selection', id: 'demo', x: 500, y: 260, expiresMs: 15000 });
  send({ type: 'capture', surface: 'toolbar' });
  console.log(await wait('captured:toolbar'));
  send({ type: 'begin', id: 'demo', action: 'explain', text: 'A good interface stays out of the way until you need it.', x: 500, y: 320 });
  send({ type: 'started', id: 'demo', route: { model: '演示模型（未发起请求）' } });
  send({ type: 'delta', id: 'demo', text: '好的界面，在你需要时出现，其余时间不打扰。\n\n### 这句话在说什么\n- **按需出现**：选中文字后，再决定是否翻译、解释或总结。\n- **保持专注**：点击时结果出现在前方，生成过程中不会反复抢焦点。\n- **明确边界**：点击之后才调用模型，不把选区写入聊天记录。\n\n> 这是一份界面演示，没有读取真实选区或调用 API。' });
  send({ type: 'done', id: 'demo' });
  send({ type: 'capture', surface: 'panel' });
  console.log(await wait('captured:panel'));
  send({ type: 'show-settings' });
  send({ type: 'capture', surface: 'settings' });
  console.log(await wait('captured:settings'));
  send({ type: 'begin', id: 'cancel-test', action: 'translate', text: 'sample', x: 500, y: 320 });
  send({ type: 'delta', id: 'cancel-test', text: 'partial' });
  send({ type: 'demo-stop' });
  send({ type: 'delta', id: 'cancel-test', text: 'MUST_NOT_APPEAR' });
  send({ type: 'done', id: 'cancel-test' });
  send({ type: 'demo-state', nonce: 'cancel' });
  const stopped = await wait('state:cancel');
  if (stopped.runId !== null || stopped.busy || stopped.answer !== 'partial') throw new Error('Late frames changed a stopped result');
  send({ type: 'demo-close' });
  send({ type: 'begin', id: 'reopened', action: 'summarize', text: 'sample', x: 500, y: 320 });
  send({ type: 'demo-state', nonce: 'reopen' });
  const reopened = await wait('state:reopen');
  if (!reopened.visible || reopened.runId !== 'reopened') throw new Error('Native close path broke window reuse');
  send({ type: 'demo-pin' }); const pinned = await state();
  send({ type: 'begin', id: 'pinned-next', action: 'translate', text: 'second click', x: 20, y: 20 });
  const stable = await state(); assert.equal(stable.x, pinned.x); assert.equal(stable.y, pinned.y); assert(stable.topmost);
  send({ type: 'demo-pin' }); send({ type: 'begin', id: 'unpinned', action: 'translate', text: 'third click', x: 100, y: 100 });
  assert.equal((await state()).topmost, false);
  send({ type: 'demo-close' });

  // Paused mode: opening and typing produce no request until Submit is invoked.
  send({ type: 'settings', settings: validateSettings({ enabled: false, theme: 'light' }) });
  send({ type: 'show-quick' }); send({ type: 'demo-input', surface: 'quick', text: 'The best tools make everyday work feel simple.\n\nPaste text from your OCR app here, then translate.' });
  await state('quick'); assert.equal(seen.has('submit'), false);
  send({ type: 'demo-submit', surface: 'quick' });
  const submitted = await wait('submit'); assert.equal(submitted.surface, 'quick'); assert.equal(submitted.action, 'translate');
  send({ type: 'input-error', surface: 'quick', id: 'older-request', message: 'stale error' });
  send({ ...submitted, type: 'begin', source: 'manual' });
  send({ type: 'delta', surface: 'quick', id: submitted.id, text: '好工具，让日常工作更轻松。\n\n将 OCR 应用提取的文字粘贴到这里，再点击翻译。' });
  send({ type: 'done', surface: 'quick', id: submitted.id });
  console.log(await capture('quick'));
  const quick = await state('quick'); assert(quick.answer.includes('好工具'));
  send({ type: 'demo-close', surface: 'quick' }); send({ type: 'show-quick' });
  const retained = await state('quick'); assert.equal(retained.input, quick.input); assert.equal(retained.answer, quick.answer);

  const beforeMinimize = await state('quick');
  send({ type: 'demo-minimize', surface: 'quick' }); assert.equal((await state('quick')).minimized, true);
  send({ type: 'show-quick' }); const restored = await state('quick'); assert.equal(restored.minimized, false); assert.equal(restored.x, beforeMinimize.x); assert.equal(restored.y, beforeMinimize.y);

  send({ type: 'demo-opacity', surface: 'quick', percent: 63 });
  const transparent = await state('quick'); assert.equal(transparent.opacityPercent, 63); assert.equal(transparent.contentOpacity, 0.63);
  assert.equal((await state('selection')).opacityPercent, 100, 'opacity is per window');
  send({ type: 'demo-close', surface: 'quick' }); send({ type: 'show-quick' });
  assert.equal((await state('quick')).contentOpacity, 0.63, 'reopening preserves chosen opacity');
  send({ type: 'demo-opacity', surface: 'quick', percent: 5 }); assert.equal((await state('quick')).contentOpacity, 0.2);
  send({ type: 'demo-opacity', surface: 'quick', percent: 200 }); assert.equal((await state('quick')).contentOpacity, 1);

  send({ type: 'settings', settings: validateSettings({ enabled: true, theme: 'dark' }) });
  console.log(await capture('quick', 'quick-dark'));
  console.log(await capture('settings', 'settings-dark'));
  send({ type: 'selection', id: 'dark-toolbar', x: 500, y: 260, expiresMs: 15000 });
  console.log(await capture('toolbar', 'toolbar-dark'));
  send({ type: 'demo-close', surface: 'quick' });

  seen.delete('submit');
  send({ type: 'show-quick' }); send({ type: 'demo-submit', surface: 'quick' });
  const pending = await wait('submit');
  send({ type: 'demo-close', surface: 'quick' }); send({ ...pending, type: 'begin', source: 'manual' });
  assert.equal((await state('quick')).visible, false, 'late begin must not reopen a closed input window');

  send({ type: 'demo-monitors' });
  const { screens } = await wait('demo-monitors');
  const placements = [];
  for (const screen of screens) for (const fx of [0.01, 0.5, 0.99]) for (const fy of [0.01, 0.5, 0.99]) {
    const x = Math.round(screen.x + screen.width * fx), y = Math.round(screen.y + screen.height * fy);
    send({ type: 'selection', id: 'position-' + ++nonce, x, y, expiresMs: 15000 });
    const actual = await state('toolbar');
    assert(actual.x >= screen.x && actual.y >= screen.y);
    assert(actual.x + actual.width <= screen.x + screen.width + 1);
    assert(actual.y + actual.height <= screen.y + screen.height + 1);
    assert(Math.abs(actual.x - x) <= actual.width + 30 && Math.abs(actual.y - y) <= actual.height + 35);
    if (fx === 0.5 && fy === 0.5) {
      const scale = actual.dpi / 96;
      assert(Math.abs(actual.x + actual.width - 6 * scale - x) <= 1, 'toolbar face ends at the selection release point');
      assert(Math.abs(actual.y + 6 * scale - y - 8 * scale) <= 1, 'toolbar face sits 8 DIPs below the selection release point');
    }
    placements.push({ anchor: { x, y }, rect: { x: actual.x, y: actual.y, width: actual.width, height: actual.height }, dpi: actual.dpi });
  }
  await writeFile(join(output, 'placement.json'), JSON.stringify({ screens, placements }, null, 2));
  console.log('Manual input, pause, theme, pinning, close/pending, and ' + placements.length + ' native placements passed.');
  console.log('Stop/late-frame and native-close/reopen regressions passed.');
  console.log('UI-only smoke passed. Screenshots: ' + output);
} finally {
  clearTimeout(timeout);
  if (child.exitCode === null) {
    const exited = once(child, 'exit'); send({ type: 'shutdown' });
    const deadline = setTimeout(() => child.kill(), 2000); await exited; clearTimeout(deadline);
  }
}
