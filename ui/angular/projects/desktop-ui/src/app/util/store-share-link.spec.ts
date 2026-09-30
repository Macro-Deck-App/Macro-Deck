import { storeShareUrl } from './store-share-link';

describe('storeShareUrl', () => {
  it('is the canonical https address on the Store site', () => {
    expect(storeShareUrl('com.acme.hue-bridge')).toBe('https://store.macro-deck.app/com.acme.hue-bridge');
  });

  it('never lets an id change the origin or add a path, query or fragment', () => {
    const url = new URL(storeShareUrl('a.b/../../evil?x=1#y'));

    expect(url.origin).toBe('https://store.macro-deck.app');
    expect(url.pathname.split('/').length).toBe(2);
    expect(url.search).toBe('');
    expect(url.hash).toBe('');
  });
});
