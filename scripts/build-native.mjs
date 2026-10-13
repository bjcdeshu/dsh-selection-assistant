import { spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { readFile, mkdir, access, rename, readdir, copyFile } from 'node:fs/promises';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { helperEnvironment } from '../src/environment.js';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export async function buildNative(options = {}) {
  if (process.platform !== 'win32') throw new Error('原生界面目前仅支持 Windows');
  const sources = (await readdir(join(root, 'native'))).filter(name => name.endsWith('.cs')).sort().map(name => join(root, 'native', name));
  const manifest = join(root, 'native', 'app.manifest');
  const config = join(root, 'native', 'app.config');
  const fingerprint = createHash('sha256');
  for (const file of [...sources, manifest, config]) fingerprint.update(file.slice(root.length)).update(await readFile(file));
  const hash = fingerprint.digest('hex').slice(0, 16);
  const dir = join(options.outDir || join(root, 'dist', 'native'), hash);
  const executable = join(dir, 'SelectionAssistant.UI.exe');
  try { await access(executable); await access(executable + '.config'); return executable; } catch { /* Build from the audited source. */ }
  const framework = join(process.env.WINDIR || 'C:\\Windows', 'Microsoft.NET', 'Framework64', 'v4.0.30319');
  const compiler = join(framework, 'csc.exe');
  try { await access(compiler); } catch { throw new Error('需要 Windows 自带的 .NET Framework 4.x C# 编译器（不支持精简版系统）。'); }
  await mkdir(dir, { recursive: true });
  const temporary = join(dir, 'build-' + randomUUID() + '.exe');
  const refs = ['System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll', 'WPF/UIAutomationClient.dll', 'WPF/UIAutomationTypes.dll'];
  await new Promise((accept, reject) => {
    const child = spawn(compiler, ['/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/out:' + temporary, '/win32manifest:' + manifest, ...refs.map(r => '/reference:' + join(framework, r)), ...sources], { windowsHide: true, stdio: 'inherit', env: helperEnvironment() });
    const timeout = setTimeout(() => { child.kill(); reject(new Error('原生界面编译超时')); }, 30000);
    child.once('error', error => { clearTimeout(timeout); reject(error); });
    child.once('exit', code => { clearTimeout(timeout); code === 0 ? accept() : reject(new Error('原生界面编译失败，退出码 ' + code)); });
  });
  await copyFile(config, executable + '.config');
  await rename(temporary, executable);
  return executable;
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { console.log(await buildNative()); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
