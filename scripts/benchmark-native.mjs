import { spawn, execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { writeFile, mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { once } from 'node:events';
import { buildNative } from './build-native.mjs';
import { attachFrames, sendFrame } from '../src/protocol.js';
import { validateSettings } from '../src/settings.js';
import { helperEnvironment } from '../src/environment.js';

// Repeatable renderer benchmark. Does not measure the hook/DSH or contact a model.
const execute = promisify(execFile);
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const executable = process.argv[3] || await buildNative();
const child = spawn(executable, ['--demo'], { windowsHide: true, stdio: ['pipe', 'pipe', 'inherit'], env: helperEnvironment() });
const send = frame => sendFrame(child.stdin, frame);
let ready;
const booted = new Promise(resolve => { ready = resolve; });
attachFrames(child.stdout, frame => { if (frame.type === 'ready') ready(); });
const samples = [];
async function sample(stage) {
  const { stdout } = await execute('powershell.exe', ['-NoProfile', '-Command', `$p=Get-Process -Id ${child.pid}; [pscustomobject]@{workingSetMiB=[math]::Round($p.WorkingSet64/1MB,2);privateMiB=[math]::Round($p.PrivateMemorySize64/1MB,2);cpuSeconds=$p.TotalProcessorTime.TotalSeconds} | ConvertTo-Json -Compress`], { windowsHide: true });
  samples.push({ stage, ...JSON.parse(stdout) });
}
const timeout = setTimeout(() => child.kill(), 45000);
try {
  await Promise.race([booted, once(child, 'exit').then(() => { throw new Error('UI exited before ready'); })]);
  send({ type: 'settings', settings: validateSettings({ enabled: true }) });
  await delay(1000); await sample('idle');
  send({ type: 'begin', id: 'bench', action: 'translate', text: 'Synthetic benchmark text.', x: 400, y: 200 });
  await delay(300); await sample('opened');
  for (let i = 0; i < 150; i++) {
    send({ type: 'delta', id: 'bench', text: 'A quiet interface leaves room for the words. 清晰的界面，让内容成为主角。\n' });
    await delay(30);
  }
  send({ type: 'done', id: 'bench' }); await delay(500); await sample('streamed');
  for (let i = 0; i < 10; i++) {
    send({ type: 'demo-close' });
    send({ type: 'begin', id: 'repeat-' + i, action: 'translate', text: 'Synthetic sample.', x: 400, y: 200 });
    send({ type: 'delta', id: 'repeat-' + i, text: 'Short translation. 简短译文。' }); send({ type: 'done', id: 'repeat-' + i });
    await delay(70);
  }
  send({ type: 'demo-close' }); await delay(1000); await sample('closed-10-cycles');
  const output = resolve(process.argv[2] || '.local/benchmark-native.json');
  await mkdir(resolve('.local'), { recursive: true });
  await writeFile(output, JSON.stringify({ executable, scope: 'native UI only; 150 chunks at 30ms, 10 reopen cycles; no forced GC or working-set trim', samples }, null, 2));
  console.log(JSON.stringify({ output, samples }));
} finally {
  clearTimeout(timeout);
  if (child.exitCode === null) { const end = once(child, 'exit'); send({ type: 'shutdown' }); const timer = setTimeout(() => child.kill(), 2000); await end; clearTimeout(timer); }
}
