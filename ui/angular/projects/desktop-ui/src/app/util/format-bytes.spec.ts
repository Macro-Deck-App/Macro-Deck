import { formatBytes } from './format-bytes';

describe('formatBytes', () => {
  it('reports plain bytes below a kilobyte', () => {
    expect(formatBytes(0)).toBe('0 B');
    expect(formatBytes(999)).toBe('999 B');
  });

  it('steps up through the binary units', () => {
    expect(formatBytes(1024)).toBe('1.0 KB');
    expect(formatBytes(1024 * 1024)).toBe('1.0 MB');
    expect(formatBytes(1024 * 1024 * 1024)).toBe('1.0 GB');
  });

  // A backup list is scanned, not read: one decimal is useful at 1.4 GB and noise at 512 MB.
  it('drops the decimal once the number is big enough to read at a glance', () => {
    expect(formatBytes(1.4 * 1024 * 1024 * 1024)).toBe('1.4 GB');
    expect(formatBytes(512 * 1024 * 1024)).toBe('512 MB');
  });

  it('renders nothing for a missing or nonsensical size', () => {
    expect(formatBytes(null)).toBe('');
    expect(formatBytes(undefined)).toBe('');
    expect(formatBytes(-1)).toBe('');
    expect(formatBytes(Number.NaN)).toBe('');
  });
});
