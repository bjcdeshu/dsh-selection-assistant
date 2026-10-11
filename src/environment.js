// Helpers need OS paths, not model keys, proxy passwords, NODE_OPTIONS or DSH
// authentication. Use an allowlist instead of guessing every credential name.
const ALLOWED = new Set(['systemroot', 'windir', 'comspec', 'path', 'pathext', 'temp', 'tmp', 'userprofile', 'localappdata', 'appdata', 'homedrive', 'homepath', 'programdata', 'programfiles', 'programfiles(x86)', 'commonprogramfiles', 'commonprogramfiles(x86)', 'processor_architecture', 'processor_identifier', 'number_of_processors', 'lang']);
export function helperEnvironment(source = process.env, nodeMode = false) {
  const env = Object.fromEntries(Object.entries(source).filter(([key]) => ALLOWED.has(key.toLowerCase())));
  if (nodeMode) env.ELECTRON_RUN_AS_NODE = '1';
  return env;
}
