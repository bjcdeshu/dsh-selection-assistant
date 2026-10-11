import { spawn } from 'node:child_process';
import { mkdir } from 'node:fs/promises';
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
  const key = message.type === 'captured' ? 'captured:' + message.surface : message.type === 'demo-state' ? 'state:' + message.nonce : message.type;
  seen.set(key, message); waiting.get(key)?.(message); waiting.delete(key);
}, error => { console.error(error.message); child.kill(); });
const closed = new Promise((_, reject) => child.once('exit', code => reject(new Error('Native UI exited early (' + code + ')'))));
closed.catch(() => {});
const wait = key => Promise.race([closed, seen.has(key) ? Promise.resolve(seen.get(key)) : new Promise(resolve => waiting.set(key, resolve))]);
const timeout = setTimeout(() => { console.error('Native UI smoke timed out'); child.kill(); process.exitCode = 1; }, 20000);
try {
  await wait('ready');
  send({ type: 'settings', settings: validateSettings({ enabled: true }) });
  send({ type: 'selection', id: 'demo', x: 500, y: 260, expiresMs: 15000 });
  send({ type: 'capture', surface: 'toolbar' });
  console.log(await wait('captured:toolbar'));
  send({ type: 'begin', id: 'demo', action: 'explain', text: 'A good interface stays out of the way until you need it.', x: 500, y: 320 });
  send({ type: 'started', id: 'demo', route: { model: '演示模型（未发起请求）' } });
  send({ type: 'delta', id: 'demo', text: '好的界面，在你需要时出现，其余时间不打扰。\n\n### 这句话在说什么\n- **按需出现**：选中文字后，再决定是否翻译、解释或总结。\n- **保持专注**：小窗不抢走原应用的焦点，结果可以留下慢慢阅读。\n- **明确边界**：点击之后才调用模型，不把选区写入聊天记录。\n\n> 这是一份界面演示，没有读取真实选区或调用 API。' });
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
  console.log('Stop/late-frame and native-close/reopen regressions passed.');
  console.log('UI-only smoke passed. Screenshots: ' + output);
} finally {
  clearTimeout(timeout);
  if (child.exitCode === null) {
    const exited = once(child, 'exit'); send({ type: 'shutdown' });
    const deadline = setTimeout(() => child.kill(), 2000); await exited; clearTimeout(deadline);
  }
}
