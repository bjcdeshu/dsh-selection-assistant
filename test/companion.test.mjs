import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { EventEmitter } from 'node:events';
import vm from 'node:vm';
import { validateSettings } from '../src/settings.js';
import { normalizeSelection, SelectionState } from '../src/selection.js';
import { ACTIONS } from '../src/prompts.js';

/** Run the actual companion logic with fake pipes/hook. Never spawns or listens. */
async function harness() {
  const handlers = new Map(), messages = [], timers = new Map(); let clock = 0, hook, starts = 0;
  const fakeProcess = Object.assign(new EventEmitter(), { stdin: new EventEmitter(), stdout: new EventEmitter(), env: {}, exit: () => {} });
  const ui = Object.assign(new EventEmitter(), { stdin: new EventEmitter(), stdout: {}, stderr: new EventEmitter(), exitCode: null, kill: () => {} });
  class Hook extends EventEmitter {
    static FilterMode = { EXCLUDE_LIST: 2 };
    constructor() { super(); hook = this; }
    start() { starts++; return true; } stop() {} cleanup() {}
  }
  const code = (await readFile(new URL('../src/companion.js', import.meta.url), 'utf8')).replace(/^import .*;\r?\n/gm, '');
  const sandbox = {
    createRequire: () => () => Hook, spawn: () => ui, process: fakeProcess,
    attachFrames: (stream, handle) => { handlers.set(stream, handle); },
    sendFrame: (stream, message) => { messages.push({ to: stream === fakeProcess.stdout ? 'host' : 'ui', ...message }); },
    validateSettings, normalizeSelection, SelectionState, ACTIONS, helperEnvironment: () => ({}),
    setTimeout: fn => { const id = ++clock; timers.set(id, fn); return id; }, clearTimeout: id => timers.delete(id),
  };
  vm.runInNewContext(code.replace('import.meta.url', '"test"'), sandbox, { filename: 'companion.vm.js' });
  const host = message => handlers.get(fakeProcess.stdin)(message);
  const surface = message => handlers.get(ui.stdout)(message);
  host({ type: 'init', nativeExe: 'fake.exe', settings: validateSettings({ enabled: true }) });
  const select = text => {
    hook.emit('text-selection', { text, programName: 'notepad.exe', endBottom: { x: 1, y: 2 } });
    for (const [id, fn] of [...timers]) { timers.delete(id); fn(); }
    return messages.filter(x => x.to === 'ui' && x.type === 'selection').at(-1).id;
  };
  return { host, surface, select, messages, starts: () => starts };
}

// Regression proven by review: cancelling A after B was clicked cancelled B.
test('late cancellation is bound to the requested id, not the latest run', async () => {
  const h = await harness();
  const a = h.select('first'); h.surface({ type: 'action', id: a, action: 'translate' });
  const b = h.select('second'); h.surface({ type: 'action', id: b, action: 'explain' });
  h.surface({ type: 'cancel', id: a });
  assert.equal(h.messages.filter(x => x.to === 'host' && x.type === 'cancel').length, 0);
  h.host({ type: 'delta', id: b, text: 'B result' });
  assert.equal(h.messages.at(-1).text, 'B result');
  h.surface({ type: 'cancel', id: b });
  assert.equal(h.messages.at(-1).id, b);
  const before = h.messages.length; h.host({ type: 'delta', id: b, text: 'late' }); assert.equal(h.messages.length, before);
});

test('dismiss from the old toolbar does not erase the new selection', async () => {
  const h = await harness(); const a = h.select('first'); const b = h.select('second');
  h.surface({ type: 'dismiss', id: a }); h.surface({ type: 'action', id: b, action: 'summarize' });
  assert.equal(h.messages.filter(x => x.to === 'host' && x.type === 'run').at(-1).text, 'second');
});

test('selecting alone never sends text to the model host', async () => {
  const h = await harness(); h.select('private selection not submitted');
  assert.equal(h.messages.filter(x => x.to === 'host' && x.type === 'run').length, 0);
  assert(!JSON.stringify(h.messages).includes('private selection not submitted'));
});
test('manual translation works while paused and never starts a selection hook', async () => {
  const h = await harness();
  h.host({ type: 'settings', settings: validateSettings({ enabled: false }) });
  const starts = h.starts();
  h.surface({ type: 'submit', id: 'manual-1', surface: 'quick', action: 'translate', text: ' OCR text ' });
  const run = h.messages.filter(x => x.to === 'host' && x.type === 'run').at(-1);
  assert.equal(run.source, 'manual'); assert.equal(run.surface, 'quick'); assert.equal(run.text, 'OCR text'); assert.equal(run.id, 'manual-1');
  assert.equal(h.starts(), starts);
});
test('concurrent quick and selected requests keep their output and cancellation separate', async () => {
  const h = await harness(); const selection = h.select('selected text');
  h.surface({ type: 'action', id: selection, action: 'explain' });
  h.surface({ type: 'submit', id: 'quick-1', surface: 'quick', action: 'translate', text: 'typed text' });
  h.host({ type: 'delta', id: selection, text: 'selected output' }); assert.equal(h.messages.at(-1).surface, 'selection');
  h.host({ type: 'delta', id: 'quick-1', text: 'manual output' }); assert.equal(h.messages.at(-1).surface, 'quick');
  h.surface({ type: 'cancel', id: selection });
  const before = h.messages.length; h.host({ type: 'delta', id: selection, text: 'late' }); assert.equal(h.messages.length, before);
  h.host({ type: 'done', id: 'quick-1' }); assert.equal(h.messages.at(-1).surface, 'quick');
  const finished = h.messages.length; h.host({ type: 'delta', id: 'quick-1', text: 'late after done' }); assert.equal(h.messages.length, finished);
});
test('invalid manual input is rejected locally and appearance changes do not restart the hook', async () => {
  const h = await harness(); const starts = h.starts();
  h.host({ type: 'settings', settings: validateSettings({ enabled: true, theme: 'dark' }) }); assert.equal(h.starts(), starts);
  for (const text of ['', ' ', 'x'.repeat(12001), 'a\x00b']) h.surface({ type: 'submit', id: 'bad', surface: 'quick', action: 'translate', text });
  assert.equal(h.messages.filter(x => x.to === 'host' && x.type === 'run').length, 0);
  assert.equal(h.messages.filter(x => x.type === 'input-error').length, 4);
});
