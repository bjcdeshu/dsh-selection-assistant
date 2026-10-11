import { spawn } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { SettingsStore } from './settings.js';
import { attachFrames, sendFrame } from './protocol.js';
import { modelCatalog, runAction, UserFacingError } from './model.js';
import { buildNative } from '../scripts/build-native.mjs';
import { isBlocked } from './selection.js';
import { helperEnvironment } from './environment.js';

export const name = 'selection-assistant';
export const inject = ['llm'];

export async function apply(ctx) {
  const logger = typeof ctx.logger === 'function' ? ctx.logger(name) : console;
  if (process.platform !== 'win32') { logger.warn('划词助手目前仅支持 Windows；未启动任何监听。'); return; }
  const store = new SettingsStore();
  await store.load();
  const nativeExe = await buildNative({ outDir: join(dirname(store.path), 'native-cache') });
  ctx.effect(() => {
    const child = spawn(process.execPath, [fileURLToPath(new URL('./companion.js', import.meta.url))], {
      windowsHide: true,
      env: helperEnvironment(process.env, true),
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    let disposed = false, active = null;
    const send = message => sendFrame(child.stdin, message);
    const abort = () => { active?.controller.abort(); active = null; };
    child.stdin.on('error', () => {});
    // Native/companion diagnostics are intentionally not relayed verbatim: they
    // could contain selected text. Only explicit protocol error codes are logged.
    child.stderr.on('data', () => {});
    child.on('error', () => logger.warn('划词伴生进程未能启动。'));
    child.on('exit', () => { abort(); if (!disposed) logger.info?.('划词助手已停止；重新启用插件可启动。'); });
    const detach = attachFrames(child.stdout, message => { handle(message).catch(() => send({ type: 'notice', message: '操作未完成，请检查模型或划词设置。' })); }, () => { abort(); child.kill(); });
    async function handle(message) {
      if (disposed) return;
      if (message.type === 'ready') send({ type: 'init', nativeExe, settings: store.value });
      else if (message.type === 'settings-get') {
        const models = await modelCatalog(ctx);
        if (!disposed) send({ type: 'settings', settings: store.value, models });
      }
      else if (message.type === 'settings-save') {
        const next = await store.save(message.settings);
        if (active) send({ type: 'failed', id: active.id, message: '设置已更新，当前请求已停止。' });
        abort(); send({ type: 'settings', settings: next });
      } else if (message.type === 'cancel') {
        if (!message.id || active?.id === message.id) abort();
      } else if (message.type === 'run') {
        const s = store.value;
        if (!s.enabled || isBlocked(message.programName, s.blockedApps) || typeof message.id !== 'string' || message.id.length > 64) return;
        abort();
        const run = { id: message.id, controller: new AbortController() }; active = run;
        try {
          await runAction(ctx, s, message, run.controller.signal, event => {
            if (active === run && !disposed) send({ ...event, id: run.id });
          });
        } catch (error) {
          if (active === run && !disposed) send({ type: 'failed', id: run.id, message: run.controller.signal.aborted ? '已停止' : /timeout|abort/i.test(error.name) ? '请求超时，请重试或切换模型。' : error instanceof UserFacingError ? error.message : '请求失败，请检查 DSH 模型设置或稍后重试。' });
        } finally { if (active === run) active = null; }
      } else if (message.type === 'fatal') logger.warn('划词助手停止（' + String(message.code).replace(/[^a-z0-9_-]/gi, '').slice(0, 60) + '）。');
    }
    return async () => {
      disposed = true; abort(); send({ type: 'shutdown' });
      await new Promise(resolve => {
        if (child.exitCode !== null) return resolve();
        const timer = setTimeout(() => {
          if (child.exitCode === null && child.pid) {
            // Only this still-owned process tree, never name-based taskkill.
            const killer = spawn('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
            killer.on('error', () => {});
          }
          resolve();
        }, 2500);
        child.once('exit', () => { clearTimeout(timer); resolve(); });
      });
      detach();
    };
  });
}
