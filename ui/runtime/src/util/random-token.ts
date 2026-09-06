// Not a security primitive - this is only collision avoidance for connection-local ids. The ladder
// exists for the baseline: randomUUID is Safari 15.4, getRandomValues is absent on Android 4.3.
export function randomToken(): string {
  const source = typeof crypto === 'undefined' ? undefined : crypto;
  if (source && typeof source.randomUUID === 'function') return source.randomUUID();
  if (source && typeof source.getRandomValues === 'function') {
    const bytes = new Uint8Array(8);
    source.getRandomValues(bytes);
    let hex = '';
    for (let index = 0; index < bytes.length; index++) hex += (bytes[index] + 0x100).toString(16).slice(1);
    return hex;
  }
  return Math.random().toString(36).slice(2, 10) + Math.random().toString(36).slice(2, 10);
}
