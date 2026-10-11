import { mkdir, readFile, writeFile, rename } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { homedir } from 'node:os';
import { randomUUID } from 'node:crypto';
import { ACTIONS, DEFAULT_PROMPTS } from './prompts.js';

export const DEFAULT_SETTINGS = Object.freeze({
  enabled: false,
  clipboardFallback: false,
  trigger: 'auto',
  targetLanguage: 'auto',
  provider: '',
  model: '',
  timeoutMs: 45000,
  maxChars: 12000,
  maxTokens: 3072,
  toolbarDelayMs: 100,
  toolbarTimeoutMs: 5000,
  fontSize: 15,
  blockedApps: ['WindowsTerminal.exe', 'cmd.exe', 'powershell.exe', 'pwsh.exe', 'KeePass.exe', 'KeePassXC.exe', '1Password.exe', 'Bitwarden.exe', 'SelectionAssistant.UI.exe'],
  prompts: DEFAULT_PROMPTS,
});

export function validateSettings(input, base = DEFAULT_SETTINGS) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new Error('设置必须是对象');
  for (const key of Object.keys(input)) if (!Object.hasOwn(DEFAULT_SETTINGS, key)) throw new Error('未知设置字段: ' + key);
  const result = { ...base, ...input, prompts: { ...base.prompts, ...(input.prompts ?? {}) } };
  for (const key of ['enabled', 'clipboardFallback']) if (typeof result[key] !== 'boolean') throw new Error(key + ' 必须是布尔值');
  if (!['auto', 'hotkey'].includes(result.trigger)) throw new Error('无效触发方式');
  for (const key of ['targetLanguage', 'provider', 'model']) {
    if (typeof result[key] !== 'string' || result[key].length > 160 || /[\r\n\x00]/.test(result[key])) throw new Error('无效 ' + key);
    result[key] = result[key].trim();
  }
  if (!result.targetLanguage) throw new Error('目标语言不能为空');
  if (!!result.provider !== !!result.model) throw new Error('提供方和模型必须同时填写，或同时留空');
  for (const [key, min, max] of [['timeoutMs', 3000, 180000], ['maxChars', 1, 30000], ['maxTokens', 128, 8192], ['toolbarDelayMs', 0, 1000], ['toolbarTimeoutMs', 1000, 20000], ['fontSize', 12, 24]]) {
    if (!Number.isInteger(result[key]) || result[key] < min || result[key] > max) throw new Error(key + ' 超出范围');
  }
  if (!Array.isArray(result.blockedApps) || result.blockedApps.length > 100 || result.blockedApps.some(x => typeof x !== 'string' || !x.trim() || x.length > 160 || /[\r\n\x00]/.test(x))) throw new Error('应用黑名单无效');
  result.blockedApps = [...new Set(result.blockedApps.map(x => x.trim()))];
  if (input.prompts !== undefined && (!input.prompts || typeof input.prompts !== 'object' || Array.isArray(input.prompts))) throw new Error('提示词设置无效');
  for (const [key, value] of Object.entries(result.prompts)) if (!ACTIONS.includes(key) || typeof value !== 'string' || value.length > 8000) throw new Error('提示词无效');
  return result;
}

export class SettingsStore {
  constructor(path = join(process.env.DSH_HOME || join(homedir(), '.dsh'), 'plugin-data', 'dsh-selection-assistant', 'settings.json')) {
    this.path = path;
    this.value = validateSettings({});
    this.queue = Promise.resolve();
  }
  async load() {
    try { this.value = validateSettings(JSON.parse(await readFile(this.path, 'utf8'))); }
    catch (error) { if (error.code !== 'ENOENT') throw new Error('划词设置读取失败；为安全起见未启动监听。请修复插件设置文件。', { cause: error }); }
    return this.value;
  }
  save(patch) {
    const operation = this.queue.then(async () => {
      const next = validateSettings(patch, this.value);
      await mkdir(dirname(this.path), { recursive: true });
      const temp = this.path + '.' + randomUUID() + '.tmp';
      await writeFile(temp, JSON.stringify(next, null, 2) + '\n', { mode: 0o600, flag: 'wx' });
      await rename(temp, this.path);
      this.value = next;
      return next;
    });
    this.queue = operation.catch(() => {});
    return operation;
  }
}
