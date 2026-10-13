import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { EventEmitter } from 'node:events';
import vm from 'node:vm';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateSettings } from '../src/settings.js';
import { isBlocked } from '../src/selection.js';
import { helperEnvironment } from '../src/environment.js';
import { UserFacingError } from '../src/model.js';

const flush = () => new Promise(resolve => setImmediate(resolve));
async function hostHarness(catalog = async () => [], execute = async () => {}) {
  const child = Object.assign(new EventEmitter(), { stdin: new EventEmitter(), stdout: new EventEmitter(), stderr: new EventEmitter(), exitCode: 0 });
  const frames = []; let receive;
  class Store {
    constructor() { this.path = '/fake/settings.json'; this.value = validateSettings({ enabled: true }); }
    async load() { return this.value; }
    async save(patch) { this.value = validateSettings(patch, this.value); return this.value; }
  }
  const code = (await readFile(new URL('../src/index.js', import.meta.url), 'utf8')).replace(/^import .*;\r?\n/gm, '').replace(/export /g, '').replaceAll('import.meta.url', JSON.stringify(new URL('../src/index.js', import.meta.url).href));
  const sandbox = {
    spawn: () => child, SettingsStore: Store, dirname, join, fileURLToPath, URL,
    process: { platform: 'win32', execPath: 'fake-node', env: {} },
    buildNative: async () => 'fake.exe', modelCatalog: catalog, runAction: execute, UserFacingError, validateSettings,
    attachFrames: (_, callback) => { receive = callback; return () => {}; },
    sendFrame: (_, value) => frames.push(value), helperEnvironment, isBlocked,
    AbortController, setTimeout, clearTimeout, console,
  };
  vm.runInNewContext(code + '\nglobalThis.start = apply;', sandbox);
  await sandbox.start({ effect: fn => fn(), logger: () => ({ warn() {}, info() {} }) });
  return { receive, frames };
}
// Regression: evaluating settings before await leaked an old enabled snapshot
// back into the companion after a newer pause had already been acknowledged.
test('slow model catalog cannot undo a newer pause or model setting', async () => {
  let resolveCatalog;
  const catalog = new Promise(resolve => { resolveCatalog = resolve; });
  const host = await hostHarness(() => catalog);
  host.receive({ type: 'settings-get' });
  host.receive({ type: 'settings-save', settings: { enabled: false, targetLanguage: 'English' } });
  await flush();
  assert.equal(host.frames.at(-1).settings.enabled, false);
  resolveCatalog([]); await flush();
  assert.equal(host.frames.at(-1).settings.enabled, false);
  assert.equal(host.frames.at(-1).settings.targetLanguage, 'English');
});
test('paused monitoring still accepts manual requests and never accepts a blocked selection', async () => {
  const requests = [];
  const host = await hostHarness(undefined, async (_, settings, request) => { requests.push({ settings, request }); });
  host.receive({ type: 'settings-save', settings: { enabled: false } }); await flush();
  host.receive({ type: 'run', id: 'manual', source: 'manual', surface: 'quick', action: 'translate', text: 'Hello', targetLanguage: '日本語' });
  host.receive({ type: 'run', id: 'selection', source: 'selection', surface: 'selection', programName: 'notepad.exe', action: 'translate', text: 'Hello' });
  await flush(); assert.equal(requests.length, 1); assert.equal(requests[0].settings.targetLanguage, '日本語');
  assert(host.frames.some(x => x.id === 'selection' && x.type === 'failed'));
});
test('host isolates two streams, replacement, late output and id-specific cancellation', async () => {
  const runs = new Map();
  const host = await hostHarness(undefined, (_, __, request, signal, emit) => new Promise(resolve => runs.set(request.id, { signal, emit, resolve })));
  const run = (id, surface) => host.receive({ type: 'run', id, source: 'manual', surface, action: 'translate', text: 'Hello' });
  run('a', 'selection'); run('b', 'quick'); await flush();
  assert.equal(runs.get('a').signal.aborted, false);
  run('c', 'quick'); await flush();
  assert.equal(runs.get('b').signal.aborted, true); assert.equal(runs.get('a').signal.aborted, false);
  const before = host.frames.length; runs.get('b').emit({ type: 'delta', text: 'stale' }); assert.equal(host.frames.length, before);
  host.receive({ type: 'cancel', id: 'b' }); assert.equal(runs.get('c').signal.aborted, false);
  runs.get('a').emit({ type: 'delta', text: 'A' }); assert.equal(host.frames.at(-1).surface, 'selection');
  runs.get('c').emit({ type: 'delta', text: 'C' }); assert.equal(host.frames.at(-1).surface, 'quick');
  host.receive({ type: 'settings-save', settings: { enabled: false } }); await flush(); assert.equal(runs.get('c').signal.aborted, false);
  host.receive({ type: 'cancel', id: 'c' }); assert.equal(runs.get('c').signal.aborted, true);
  for (const value of runs.values()) value.resolve(); await flush();
});
