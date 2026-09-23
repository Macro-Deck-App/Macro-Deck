import { storeFreshness } from './store-badges';

describe('storeFreshness', () => {
  const now = Date.parse('2026-09-23T12:00:00Z');

  it('calls a package new for the first thirty days after it was published', () => {
    expect(storeFreshness({ createdAt: '2026-09-01T00:00:00Z', updatedAt: '2026-09-20T00:00:00Z' }, now)).toBe('new');
    expect(storeFreshness({ createdAt: '2026-08-20T00:00:00Z', updatedAt: null }, now)).toBeNull();
  });

  it('calls an older package updated when a release landed in the last two weeks', () => {
    expect(storeFreshness({ createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-09-15T00:00:00Z' }, now)).toBe('updated');
    expect(storeFreshness({ createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-09-01T00:00:00Z' }, now)).toBeNull();
  });

  it('claims nothing without dates', () => {
    expect(storeFreshness({ createdAt: null, updatedAt: null }, now)).toBeNull();
    expect(storeFreshness({ createdAt: 'not a date', updatedAt: undefined }, now)).toBeNull();
  });
});
