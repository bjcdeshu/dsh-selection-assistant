import { readdir, readFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
let count = 0;
for (const dir of ['src', 'scripts', 'test']) {
  for (const name of await readdir(join(root, dir))) {
    if (!/\.(?:m?js)$/.test(name)) continue;
    await new Promise((accept, reject) => {
      const child = spawn(process.execPath, ['--check', join(root, dir, name)], { stdio: 'inherit' });
      child.on('error', reject); child.on('exit', code => code === 0 ? accept() : reject(new Error(name + ' syntax failed')));
    }); count++;
  }
}
const p = JSON.parse(await readFile(join(root, 'package.json'), 'utf8'));
if (p.dependencies['selection-hook'] !== '2.1.1') throw new Error('Review native dependency upgrades explicitly');
console.log('Syntax and package checks passed (' + count + ' files)');
