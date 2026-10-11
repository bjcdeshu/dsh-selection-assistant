export const MAX_FRAME_BYTES = 256 * 1024;

/** Bounded NDJSON. No sockets, public ports, raw text logs, or credential IPC. */
export function attachFrames(stream, onMessage, onError = () => {}) {
  let buffer = '', failed = false;
  stream.setEncoding('utf8');
  const data = chunk => {
    if (failed) return;
    buffer += chunk;
    let newline;
    while ((newline = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, newline); buffer = buffer.slice(newline + 1);
      if (Buffer.byteLength(line) > MAX_FRAME_BYTES) { failed = true; onError(new Error('IPC frame too large')); return; }
      if (!line.trim()) continue;
      try {
        const value = JSON.parse(line);
        if (!value || typeof value !== 'object' || Array.isArray(value) || typeof value.type !== 'string') throw new Error('Invalid IPC envelope');
        onMessage(value);
      } catch { failed = true; onError(new Error('Invalid IPC frame')); return; }
    }
    if (Buffer.byteLength(buffer) > MAX_FRAME_BYTES) { failed = true; onError(new Error('IPC frame too large')); }
  };
  stream.on('data', data);
  return () => stream.off('data', data);
}

export function sendFrame(stream, message) {
  if (!stream || stream.destroyed || !stream.writable) return false;
  const frame = JSON.stringify(message) + '\n';
  if (Buffer.byteLength(frame) > MAX_FRAME_BYTES) return false;
  return stream.write(frame);
}
