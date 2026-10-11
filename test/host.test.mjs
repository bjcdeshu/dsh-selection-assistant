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
async function hostHarness(catalog) {
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
    buildNative: async () => 'fake.exe', modelCatalog: catalog, runAction: async () => {}, UserFacingError,
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
