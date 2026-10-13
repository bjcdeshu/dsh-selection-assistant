import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildNative } from './build-native.mjs';
import { helperEnvironment } from '../src/environment.js';
import { attachFrames, sendFrame } from '../src/protocol.js';
import { validateSettings } from '../src/settings.js';

// Real native processes with synthetic content only. The explicit state file is
// isolated from the user's preferences; no hooks, clipboard, hotkeys or model.
if (process.platform !== 'win32') throw new Error('Window restart verification requires Windows');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = join(root, '.local', 'window-restart-' + Date.now());
const stateFile = join(output, 'window-state.json');
await mkdir(output, { recursive: true });
const executable = await buildNative();
const sourceText = 'RESTART_FIXTURE_SOURCE_MUST_NOT_BE_SAVED';
const answerText = 'RESTART_FIXTURE_ANSWER_MUST_NOT_BE_SAVED';
const expected = {
  selection: { widthDip: 590, heightDip: 440, opacityPercent: 71 },
  quick: { widthDip: 710, heightDip: 460, opacityPercent: 64 },
};
let checks = 0;
const equal = (actual, wanted, label) => { checks++; assert.equal(actual, wanted, label); };
const verifyAppearance = (state, surface, label) => {
  for (const key of ['widthDip', 'heightDip']) {
    // WPF rounds a requested half physical pixel (590 DIPs at 125%, for
    // example) to the display's grid. This is not a lost window preference.
    checks++;
    assert(Math.abs(state[key] - expected[surface][key]) <= 48 / state.dpi + 0.001,
      label + ': ' + key + ' expected ' + expected[surface][key] + ', got ' + state[key]);
  }
  equal(state.opacityPercent, expected[surface].opacityPercent, label + ': opacity percent');
  equal(state.contentOpacity, expected[surface].opacityPercent / 100, label + ': content opacity');
};

async function launch(file, cwd, previewName) {
  const preview = join(output, previewName);
  await mkdir(preview, { recursive: true });
  const child = spawn(file, ['--demo', preview, '--state-file', stateFile], {
    cwd, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env: helperEnvironment(),
  });
  const queued = new Map(), pending = new Map();
  let nonce = 0, closing = false, failure, stderr = '';
  const fail = error => {
    failure ||= error;
    for (const waiters of pending.values()) for (const waiter of waiters) waiter.reject(error);
    pending.clear();
  };
  const exited = new Promise(resolve => child.once('exit', (code, signal) => {
    clearTimeout(deadline);
    if (!closing) fail(new Error('Native UI exited early: ' + (signal || code) + (stderr ? '\n' + stderr : '')));
    resolve({ code, signal });
  }));
  const deadline = setTimeout(() => { fail(new Error('Native restart verification timed out after 20 seconds')); child.kill(); }, 20000);
  child.once('error', fail);
  child.stdin.on('error', error => { if (!closing) fail(error); });
  child.stderr.on('data', chunk => { stderr += chunk; });
  attachFrames(child.stdout, message => {
    if (message.type === 'demo-error') { fail(new Error(message.message)); return; }
    const key = message.type === 'demo-state' ? 'state:' + message.nonce : message.type;
    const waiters = pending.get(key);
    if (waiters?.length) waiters.shift().resolve(message);
    else { if (!queued.has(key)) queued.set(key, []); queued.get(key).push(message); }
  }, fail);
  const wait = key => {
    if (failure) return Promise.reject(failure);
    if (queued.get(key)?.length) return Promise.resolve(queued.get(key).shift());
    return new Promise((resolve, reject) => {
      if (!pending.has(key)) pending.set(key, []);
      pending.get(key).push({ resolve, reject });
    });
  };
  const send = message => {
    if (failure) throw failure;
    if (!sendFrame(child.stdin, message)) throw new Error('Native test input is unavailable');
  };
  const state = async surface => {
    const id = String(++nonce);
    send({ type: 'demo-state', surface, nonce: id });
    return wait('state:' + id);
  };
  const stop = async () => {
    closing = true;
    if (child.exitCode !== null || child.signalCode !== null) { clearTimeout(deadline); return; }
    if (child.stdin.writable) sendFrame(child.stdin, { type: 'shutdown' });
    const force = setTimeout(() => child.kill(), 2000);
    const result = await exited;
    clearTimeout(force);
    clearTimeout(deadline);
    if (failure) throw failure;
    assert.equal(result.code, 0, 'Native UI must shut down cleanly');
  };
  try {
    await wait('ready');
    send({ type: 'settings', settings: validateSettings({ enabled: false }) });
    return { send, wait, state, stop };
  } catch (error) {
    await stop().catch(() => {});
    throw error;
  }
}

const beginSelection = (ui, id) => {
  ui.send({ type: 'begin', surface: 'selection', id, action: 'translate', text: sourceText, x: 360, y: 220 });
  ui.send({ type: 'delta', surface: 'selection', id, text: answerText });
  ui.send({ type: 'done', surface: 'selection', id });
};
const first = await launch(executable, root, 'first');
try {
  first.send({ type: 'show-quick' });
  beginSelection(first, 'first-selection');
  for (const surface of Object.keys(expected)) {
    equal((await first.state(surface)).targetLanguage, 'auto', surface + ': automatic target default');
    const value = expected[surface];
    first.send({ type: 'demo-resize', surface, width: value.widthDip, height: value.heightDip });
    first.send({ type: 'demo-opacity', surface, percent: value.opacityPercent });
    const changed = await first.state(surface);
    verifyAppearance(changed, surface, surface + ': changed');
    expected[surface].widthDip = changed.widthDip;
    expected[surface].heightDip = changed.heightDip;
  }

  first.send({ type: 'demo-input', surface: 'quick', text: sourceText });
  first.send({ type: 'demo-language', surface: 'quick', value: 'French' });
  first.send({ type: 'demo-submit', surface: 'quick' });
  const submitted = await first.wait('submit');
  equal(submitted.surface, 'quick', 'French submit surface');
  equal(submitted.targetLanguage, 'French', 'French catalog target reaches the model request');
  equal(submitted.text, sourceText, 'French request retains source text');
  first.send({ ...submitted, type: 'begin', source: 'manual' });
  first.send({ type: 'delta', surface: 'quick', id: submitted.id, text: answerText });
  first.send({ type: 'done', surface: 'quick', id: submitted.id });
  equal((await first.state('quick')).answer, answerText, 'synthetic quick result is displayed');
  first.send({ type: 'demo-language', surface: 'quick', value: 'auto' });
  first.send({ type: 'demo-submit', surface: 'quick' });
  const automatic = await first.wait('submit');
  equal(automatic.targetLanguage, 'auto', 'automatic choice reaches the model request');
  first.send({ type: 'demo-stop', surface: 'quick' });

  // A manual retry uses the chosen menu language, while a new selection uses
  // the global target that the Host will apply to its fresh model request.
  first.send({ type: 'demo-language', surface: 'selection', value: 'French' });
  first.send({ type: 'demo-submit', surface: 'selection' });
  const selectionRetry = await first.wait('submit');
  equal(selectionRetry.surface, 'selection', 'selection retry surface');
  equal(selectionRetry.targetLanguage, 'French', 'selection retry submits the chosen catalog target');
  first.send({ ...selectionRetry, type: 'begin', source: 'manual' });
  first.send({ type: 'delta', surface: 'selection', id: selectionRetry.id, text: answerText });
  first.send({ type: 'done', surface: 'selection', id: selectionRetry.id });
  equal((await first.state('selection')).targetLanguage, 'French', 'manual begin preserves its chosen target');
  beginSelection(first, 'fresh-automatic-selection');
  equal((await first.state('selection')).targetLanguage, 'auto', 'new selection resets its caption to the automatic global target');
  first.send({ type: 'settings', settings: validateSettings({ enabled: false, targetLanguage: 'German' }) });
  first.send({ type: 'demo-language', surface: 'selection', value: 'French' });
  equal((await first.state('selection')).targetLanguage, 'French', 'window target may differ from the explicit global target');
  beginSelection(first, 'fresh-german-selection');
  equal((await first.state('selection')).targetLanguage, 'German', 'new selection aligns its caption with the explicit global target');
  first.send({ type: 'settings', settings: validateSettings({ enabled: false }) });

  for (const surface of Object.keys(expected)) {
    first.send({ type: 'demo-minimize', surface });
    equal((await first.state(surface)).minimized, true, surface + ': minimized');
    first.send({ type: 'demo-close', surface });
    equal((await first.state(surface)).visible, false, surface + ': closed while minimized');
  }
} finally { await first.stop(); }

const saved = await readFile(stateFile, 'utf8');
const savedObject = JSON.parse(saved);
equal(saved.includes(sourceText), false, 'source text is never persisted');
equal(saved.includes(answerText), false, 'answer text is never persisted');
// A window-state document must contain numeric preferences, not strings or any
// hidden input/result/history payload. Objects are only the preference schema.
const inspectValues = value => {
  if (value && typeof value === 'object' && !Array.isArray(value)) {
    for (const nested of Object.values(value)) inspectValues(nested);
  } else {
    checks++;
    assert.equal(typeof value, 'number', 'window state contains numeric preferences only');
    assert(Number.isFinite(value), 'saved preference must be finite');
  }
};
inspectValues(savedObject);

// A new executable location and working directory model an updated native
// cache. Explicit persistent state must not depend on either cache directory.
const alternateCache = join(output, 'different-cache');
const alternateCwd = join(output, 'different-working-directory');
await mkdir(alternateCache, { recursive: true });
await mkdir(alternateCwd, { recursive: true });
const secondExecutable = join(alternateCache, 'SelectionAssistant.UI.exe');
await copyFile(executable, secondExecutable);
await copyFile(executable + '.config', secondExecutable + '.config');
const restored = {};
const second = await launch(secondExecutable, alternateCwd, 'second');
try {
  second.send({ type: 'show-quick' });
  const quick = await second.state('quick');
  verifyAppearance(quick, 'quick', 'quick: independent process restore');
  equal(quick.input, '', 'quick input is not restored across processes');
  equal(quick.answer, '', 'quick answer is not restored across processes');
  equal(quick.targetLanguage, 'auto', 'automatic configured target remains default');
  restored.quick = expected.quick;
  beginSelection(second, 'second-selection');
  verifyAppearance(await second.state('selection'), 'selection', 'selection: begin preserves restored dimensions');
  restored.selection = expected.selection;
  for (const surface of Object.keys(expected)) {
    second.send({ type: 'demo-close', surface });
    if (surface === 'quick') second.send({ type: 'show-quick' });
    else beginSelection(second, 'reopened-selection');
    verifyAppearance(await second.state(surface), surface, surface + ': close and reopen');
  }
} finally { await second.stop(); }

const result = { result: 'passed', checks, independentProcesses: 2, differentCacheAndCwd: true, restored, stateFile };
await writeFile(join(output, 'result.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify({ ...result, report: join(output, 'result.json') }, null, 2));
