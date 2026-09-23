import { StoreBrowseStateService, isStoreDetailUrl, isStoreListUrl } from './store-browse-state.service';

describe('StoreBrowseStateService', () => {
  it('gives the position back once, and only to the same view of the same list', () => {
    const state = new StoreBrowseStateService();
    state.save('discover', { key: 'plugins', scrollTop: 900, itemCount: 48 });

    expect(state.restore('discover', 'icon packs')).toBeNull();

    state.save('discover', { key: 'plugins', scrollTop: 900, itemCount: 48 });
    expect(state.restore('discover', 'plugins')).toEqual({ key: 'plugins', scrollTop: 900, itemCount: 48 });
    expect(state.restore('discover', 'plugins')).toBeNull();
  });

  it('forgets a position once the list was left for somewhere other than an entry', () => {
    const state = new StoreBrowseStateService();
    state.save('discover', { key: 'k', scrollTop: 900, itemCount: 48 });

    state.forget('discover');

    expect(state.restore('discover', 'k')).toBeNull();
  });

  it('recognises a Store details page but not the Store lists', () => {
    expect(isStoreDetailUrl('/store/Plugin/com.pyflat.hotkeys')).toBeTrue();
    expect(isStoreDetailUrl('/store/IconPack/a.b')).toBeTrue();
    expect(isStoreDetailUrl('/store/installed')).toBeFalse();
    expect(isStoreDetailUrl('/store?q=x')).toBeFalse();
    expect(isStoreListUrl('/store?q=x')).toBeTrue();
    expect(isStoreListUrl('/store/installed')).toBeTrue();
    expect(isStoreListUrl('/store/tests')).toBeTrue();
    expect(isStoreListUrl('/store/Plugin/a.b')).toBeFalse();
    expect(isStoreListUrl('/integrations/a.b')).toBeFalse();
  });
});
