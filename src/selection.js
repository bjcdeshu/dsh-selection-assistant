import { randomUUID, createHash } from 'node:crypto';

export function appName(value) { return String(value ?? '').replaceAll('\\', '/').split('/').pop().toLowerCase(); }
export function isBlocked(program, blockedApps) {
  const name = appName(program);
  return name === 'selectionassistant.ui.exe' || blockedApps.some(x => appName(x) === name);
}
export function validPoint(p) { return !!p && Number.isFinite(p.x) && Number.isFinite(p.y) && p.x !== -99999 && p.y !== -99999 && Math.abs(p.x) < 100000 && Math.abs(p.y) < 100000; }

// selection-hook uses physical screen pixels. A finite UIA rectangle may still
// describe the whole control; a mouse gesture should follow its release point.
export function selectionAnchor(data, { trigger = 'auto', cursor } = {}) {
  const level = data.posLevel;
  const hasMouse = level === undefined || level >= 1;
  const hasBounds = level === undefined || level >= 3;
  const mouse = hasMouse && validPoint(data.mousePosEnd) ? data.mousePosEnd : null;
  const edge = hasBounds ? [data.endBottom, data.startBottom].find(validPoint) : null;
  const point = trigger === 'hotkey' ? edge || (validPoint(cursor) && cursor) || mouse : mouse || edge;
  return point ? { x: Math.round(point.x), y: Math.round(point.y) } : null;
}

export function normalizeSelection(data, settings, options) {
  if (!settings.enabled || !data || typeof data.text !== 'string') return null;
  if (isBlocked(data.programName, settings.blockedApps)) return null;
  const text = data.text.trim();
  if (!text || text.length > settings.maxChars || /\x00/.test(text)) return null;
  // An extra tripwire, not a promise to detect all passwords or secrets.
  if (/-----BEGIN [A-Z ]*PRIVATE KEY-----|\b(?:sk-[A-Za-z0-9_-]{24,}|gh[pousr]_[A-Za-z0-9]{30,})\b/.test(text)) return null;
  const anchor = selectionAnchor(data, options);
  if (!anchor) return null;
  return { id: randomUUID(), text, programName: String(data.programName ?? ''), x: Math.round(anchor.x), y: Math.round(anchor.y) };
}

/** Only a click on the latest, still-visible toolbar may submit its selection. */
export class SelectionState {
  constructor(now = Date.now) { this.now = now; this.current = null; this.last = null; }
  accept(selection, ttl) {
    if (!selection) return null;
    const key = createHash('sha256').update(selection.programName + '\0' + selection.text + '\0' + selection.x + ',' + selection.y).digest('hex');
    if (this.last?.key === key && this.now() - this.last.at < 400) return null;
    this.last = { key, at: this.now() };
    this.current = { ...selection, expires: this.now() + ttl };
    return this.current;
  }
  take(id) {
    const value = this.current;
    if (!value || value.id !== id) return null;
    this.clear();
    return this.now() <= value.expires ? value : null;
  }
  clear() { this.current = null; }
}
