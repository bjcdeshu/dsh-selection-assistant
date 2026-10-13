import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile, readFile, readdir } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { helperEnvironment } from '../src/environment.js';

// The fixture references the production store and runs every read/write in a
// fresh process, with different working directories representing native builds.
if (process.platform !== 'win32') throw new Error('Window preference verification requires Windows');
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = join(root, '.local', `window-preferences-verification-${Date.now()}`);
const cacheA = join(output, 'native-cache', 'build-a');
const cacheB = join(output, 'native-cache', 'build-b');
const stateFile = join(output, 'plugin-data', 'window-state.json');
await Promise.all([mkdir(cacheA, { recursive: true }), mkdir(cacheB, { recursive: true })]);
const source = join(output, 'WindowPreferencesVerification.cs');
const executable = join(output, 'WindowPreferencesVerification.exe');
await writeFile(source, String.raw`
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using DshSelectionAssistant;

internal static class WindowPreferencesVerification {
  static double Parse(string value) { return Double.Parse(value, CultureInfo.InvariantCulture); }
  static void Require(bool condition, string label) { if (!condition) throw new Exception(label); }
  static object Read(WindowPreferences store, string name) {
    var state = store.Get(name);
    return state == null ? null : (object)new { width = state.Width, height = state.Height, opacity = state.Opacity };
  }
  static int Main(string[] args) {
    try {
      // Do not rely on Dispose for the debounce case: verify its timer saves.
      var store = new WindowPreferences(args[1] == "memory" ? null : args[0]);
      if (args[1] == "write" || args[1] == "debounce" || args[1] == "dispose") {
        Require(store.Update(args[2], Parse(args[3]), Parse(args[4]), Parse(args[5])), "Update failed");
        if (args[1] == "write") Require(store.Flush(), "Flush failed: " + store.LastError);
        if (args[1] == "debounce") { Thread.Sleep(900); Require(store.LastError == null, "Debounce save failed"); }
        if (args[1] == "dispose") store.Dispose();
      } else if (args[1] == "invalid") {
        Require(!store.Update("unknown", 500, 400, 50), "Unknown surface accepted");
        Require(!store.Update("quick", Double.NaN, 400, 50), "NaN width accepted");
        Require(!store.Update("quick", 500, Double.PositiveInfinity, 50), "Infinite height accepted");
        Require(!store.Update("quick", -1, 400, 50), "Negative width accepted");
        Require(!store.Update("quick", 500, 0, 50), "Zero height accepted");
        Require(!store.Update("quick", 1e100, 400, 50), "Extreme finite width accepted");
        Require(!store.Update("quick", 500, 32768.01, 50), "Oversized height accepted");
        foreach (double opacity in new double[] { Double.NaN, Double.NegativeInfinity, 19.99, 100.01 })
          Require(!store.Update("quick", 500, 400, opacity), "Invalid opacity accepted");
        Require(store.Flush(), "Invalid inputs affected save");
      } else if (args[1] == "memory") {
        Require(store.Update("quick", 700.25, 500.5, 42), "Memory update failed");
        Require(store.Flush(), "Memory flush failed");
        store.Dispose(); store.Dispose();
        Require(!store.Update("quick", 800, 600, 50), "Disposed store accepted an update");
      } else if (args[1] == "write-failure") {
        string previous = File.ReadAllText(args[0]);
        FileAttributes attributes = File.GetAttributes(args[0]);
        try {
          File.SetAttributes(args[0], attributes | FileAttributes.ReadOnly);
          Require(store.Update("quick", 910.5, 620.75, 53.125), "Failure test update failed");
          Require(!store.Flush(), "Read-only destination was overwritten");
          Require(!String.IsNullOrEmpty(store.LastError), "Save failure had no diagnostic");
          Require(File.ReadAllText(args[0]) == previous, "Failed save damaged previous data");
        } finally { File.SetAttributes(args[0], attributes); }
        Require(store.Flush(), "Pending values were lost after failed save");
        Require(store.LastError == null, "Successful retry did not clear diagnostic");
      } else if (args[1] == "burst") {
        for (int i = 0; i < 30; i++) {
          Require(store.Update("selection", 500.25 + i, 400.75 + i, 20 + i), "Selection update failed");
          Require(store.Update("quick", 800.5 + i, 600.125 + i, 100 - i), "Quick update failed");
        }
        Require(store.Flush(), "Burst flush failed");
      } else if (args[1] != "read") throw new Exception("Unknown operation");
      Console.WriteLine(new JavaScriptSerializer().Serialize(new {
        selection = Read(store, "selection"), quick = Read(store, "quick")
      }));
      return 0;
    } catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
  }
}
`, 'utf8');

const framework = join(process.env.WINDIR || 'C:\\Windows', 'Microsoft.NET', 'Framework64', 'v4.0.30319');
const run = (file, args, cwd = cacheA) => new Promise((accept, reject) => {
  const child = spawn(file, args, { cwd, windowsHide: true, env: helperEnvironment(), stdio: ['ignore', 'pipe', 'pipe'] });
  let stdout = '', stderr = '';
  child.stdout.on('data', chunk => stdout += chunk);
  child.stderr.on('data', chunk => stderr += chunk);
  const timer = setTimeout(() => { child.kill(); reject(new Error('Window preference verification timed out')); }, 20000);
  child.once('error', error => { clearTimeout(timer); reject(error); });
  child.once('exit', code => { clearTimeout(timer); code === 0 ? accept(stdout) : reject(new Error(stderr || stdout || `Exited ${code}`)); });
});
await run(join(framework, 'csc.exe'), ['/nologo', '/target:exe', '/utf8output', `/out:${executable}`,
  `/reference:${join(framework, 'System.Web.Extensions.dll')}`,
  join(root, 'native', 'WindowPreferences.cs'), source]);
const invoke = async (operation, args = [], cwd = cacheA) => JSON.parse((await run(executable, [stateFile, operation, ...args.map(String)], cwd)).trim());
let checks = 0;
const equal = (actual, expected, label) => { assert.deepEqual(actual, expected, label); checks++; };
equal(await invoke('read'), { selection: null, quick: null }, 'Missing file uses defaults');
equal(await invoke('memory'), { selection: null, quick: { width: 700.25, height: 500.5, opacity: 42 } }, 'Missing path supports safe in-memory previews');
equal(await invoke('read'), { selection: null, quick: null }, 'Memory-only mode did not create a file');
const selection = { width: 521.25, height: 431.5, opacity: 63.75 };
await invoke('write', ['selection', ...Object.values(selection)]);
equal(await invoke('read', [], cacheB), { selection, quick: null }, 'New process and build cache preserve DIP values');
const quick = { width: 876.5, height: 612.25, opacity: 81.125 };
await invoke('debounce', ['quick', ...Object.values(quick)], cacheB);
equal(await invoke('read'), { selection, quick }, 'Debounced write merges both windows across restarts');
const beforeInvalid = await readFile(stateFile, 'utf8');
await invoke('invalid');
equal(await readFile(stateFile, 'utf8'), beforeInvalid, 'Invalid updates leave the existing file untouched');
equal(await readdir(cacheA), [], 'No state stored under first build cache');
equal(await readdir(cacheB), [], 'No state stored under second build cache');
await invoke('burst');
equal(await invoke('read', [], cacheB), {
  selection: { width: 529.25, height: 429.75, opacity: 49 },
  quick: { width: 829.5, height: 629.125, opacity: 71 },
}, 'Rapid updates retain the latest independent values');
const persisted = JSON.parse(await readFile(stateFile, 'utf8'));
equal(Object.keys(persisted).sort(), ['quick', 'selection', 'version'], 'Only known windows and format version are stored');
for (const name of ['selection', 'quick']) equal(Object.keys(persisted[name]).sort(), ['height', 'opacity', 'width'], 'Only numeric window preferences are stored');
equal((await readdir(dirname(stateFile))).filter(name => name.endsWith('.tmp')), [], 'Atomic saves leave no temporary files');
await invoke('write-failure');
equal((await invoke('read', [], cacheB)).quick, { width: 910.5, height: 620.75, opacity: 53.125 }, 'Write failures preserve data and allow pending values to retry');
equal((await readdir(dirname(stateFile))).filter(name => name.endsWith('.tmp')), [], 'Failed atomic saves clean up temporary files');

const validQuick = { width: 960.5, height: 710.75, opacity: 100 };
for (const invalid of [
  { width: '500', height: 400, opacity: 50 },
  { width: true, height: 400, opacity: 50 },
  { width: 500, height: 0, opacity: 50 },
  { width: 1e100, height: 400, opacity: 50 },
  { width: 500, height: 32768.01, opacity: 50 },
  { width: 500, height: 400, opacity: 101 },
  { width: 500, height: 400 },
]) {
  await writeFile(stateFile, JSON.stringify({ version: 1, selection: invalid, quick: validQuick }));
  equal(await invoke('read'), { selection: null, quick: validQuick }, 'Invalid window record cannot discard the valid other window');
}
for (const contents of ['{bad json', 'null', '[]', '{"version":2,"quick":{"width":800,"height":600,"opacity":70}}',
  '{"version":1,"selection":{"width":1e999,"height":400,"opacity":50}}', ' '.repeat(16385)]) {
  await writeFile(stateFile, contents);
  equal(await invoke('read'), { selection: null, quick: null }, 'Corrupt, non-finite, oversized and unsupported files use defaults');
}
await invoke('dispose', ['selection', 620.5, 470.25, 20]);
equal(await invoke('read', [], cacheB), { selection: { width: 620.5, height: 470.25, opacity: 20 }, quick: null }, 'Dispose flush recovers a corrupt file');
await invoke('write', ['quick', 32768, 32768, 100]);
equal((await invoke('read', [], cacheB)).quick, { width: 32768, height: 32768, opacity: 100 }, 'Exact maximum size remains valid for later screen clamping');
const report = { result: 'passed', checks, independentProcesses: true, separateBuildDirectories: true, report: join(output, 'result.json') };
await writeFile(report.report, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report, null, 2));
