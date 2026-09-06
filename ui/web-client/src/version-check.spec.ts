import {
  decideVersionAction,
  readUiCommit,
  runVersionCheck,
  showOutdatedUiError,
  UI_COMMIT_META,
  withCacheBust,
  withoutCacheBust,
  type VersionCheckDeps,
} from './version-check';

function stampCommit(commit: string | null): void {
  const existing = document.querySelector('meta[name="' + UI_COMMIT_META + '"]');
  if (existing && existing.parentNode) existing.parentNode.removeChild(existing);
  if (commit === null) return;
  const meta = document.createElement('meta');
  meta.setAttribute('name', UI_COMMIT_META);
  meta.content = commit;
  document.head.appendChild(meta);
}

function memoryStorage(seed?: { [key: string]: string }): Pick<Storage, 'getItem' | 'setItem' | 'removeItem'> {
  const values: { [key: string]: string } = seed === undefined ? {} : seed;
  return {
    getItem: (key: string) => (Object.prototype.hasOwnProperty.call(values, key) ? values[key] : null),
    setItem: (key: string, value: string) => {
      values[key] = value;
    },
    removeItem: (key: string) => {
      delete values[key];
    },
  };
}

interface Recorder {
  deps: VersionCheckDeps;
  replaced: string[];
  rewritten: string[];
}

function deps(
  url: string,
  hostCommit: string | null,
  storage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>,
  overrides?: Partial<VersionCheckDeps>,
): Recorder {
  const replaced: string[] = [];
  const rewritten: string[] = [];
  const base: VersionCheckDeps = {
    doc: document,
    storage: storage === undefined ? memoryStorage() : storage,
    currentUrl: () => url,
    replaceUrl: target => {
      replaced.push(target);
    },
    replaceUrlInPlace: target => {
      rewritten.push(target);
    },
    fetchHostCommit: () => Promise.resolve(hostCommit),
    now: () => 1000,
  };
  const merged = { ...base, ...(overrides === undefined ? {} : overrides) };
  return { deps: merged, replaced: replaced, rewritten: rewritten };
}

describe('decideVersionAction', () => {
  it('accepts a host that reports no commit', () => {
    expect(decideVersionAction('abc1234', null, [])).toBe('ok');
  });

  it('accepts a matching commit whatever its case', () => {
    expect(decideVersionAction('ABC1234', 'abc1234', [])).toBe('ok');
  });

  it('reloads once for a commit that was never reloaded for', () => {
    expect(decideVersionAction('abc1234', 'def5678', [null, null])).toBe('reload');
  });

  it('reports rather than looping when the session marker records the attempt', () => {
    expect(decideVersionAction('abc1234', 'def5678', ['def5678', null])).toBe('report');
  });

  it('reports when only the reload parameter records the attempt', () => {
    expect(decideVersionAction('abc1234', 'def5678', [null, 'def5678'])).toBe('report');
  });

  it('reloads again for a commit other than the one already tried', () => {
    expect(decideVersionAction('abc1234', 'ghi9012', ['def5678', 'def5678'])).toBe('reload');
  });
});

describe('runVersionCheck', () => {
  afterEach(() => {
    stampCommit(null);
    const overlay = document.getElementById('macro-deck-outdated-ui');
    if (overlay && overlay.parentNode) overlay.parentNode.removeChild(overlay);
  });

  it('does nothing at all in an unstamped build', async () => {
    stampCommit(null);
    const recorder = deps('http://host/', 'def5678');
    const fetchHostCommit = jasmine.createSpy('fetchHostCommit').and.resolveTo('def5678');

    await runVersionCheck(() => 'http://host', { ...recorder.deps, fetchHostCommit: fetchHostCommit });

    expect(fetchHostCommit).not.toHaveBeenCalled();
    expect(recorder.replaced).toEqual([]);
  });

  it('reloads to a cache-busted url and records the attempt', async () => {
    stampCommit('abc1234');
    const storage = memoryStorage();
    const recorder = deps('http://host/deck', 'def5678', storage);

    await runVersionCheck(() => 'http://host', recorder.deps);

    expect(recorder.replaced).toEqual([withCacheBust('http://host/deck', 'def5678')]);
    expect(storage.getItem('macro-deck.reloaded-for')).toBe('def5678');
  });

  it('waits for the reload preparation before navigating', async () => {
    stampCommit('abc1234');
    const order: string[] = [];
    const recorder = deps('http://host/deck', 'def5678', undefined, {
      prepareReload: () => {
        order.push('prepared');
        return Promise.resolve();
      },
      replaceUrl: () => {
        order.push('navigated');
      },
    });

    await runVersionCheck(() => 'http://host', recorder.deps);

    expect(order).toEqual(['prepared', 'navigated']);
  });

  it('clears the marker and the reload parameter once the commits agree', async () => {
    stampCommit('abc1234');
    const storage = memoryStorage({ 'macro-deck.reloaded-for': 'abc1234' });
    const url = withCacheBust('http://host/deck', 'abc1234');
    const recorder = deps(url, 'abc1234', storage);

    await runVersionCheck(() => 'http://host', recorder.deps);

    expect(storage.getItem('macro-deck.reloaded-for')).toBeNull();
    expect(recorder.rewritten).toEqual([withoutCacheBust(url)]);
    expect(recorder.replaced).toEqual([]);
  });

  it('does not reload a second time for a commit that already failed to heal', async () => {
    stampCommit('abc1234');
    const storage = memoryStorage({ 'macro-deck.reloaded-for': 'def5678' });
    const recorder = deps('http://host/deck', 'def5678', storage);
    spyOn(console, 'error');

    await runVersionCheck(() => 'http://host', recorder.deps);

    expect(recorder.replaced).toEqual([]);
  });

  it('shows the blocking notice with the text it was given', async () => {
    stampCommit('abc1234');
    const storage = memoryStorage({ 'macro-deck.reloaded-for': 'def5678' });
    const recorder = deps('http://host/deck', 'def5678', storage, {
      outdatedText: { heading: 'Veraltet', body: 'Cache leeren.' },
    });
    spyOn(console, 'error');

    await runVersionCheck(() => 'http://host', recorder.deps);

    const overlay = document.getElementById('macro-deck-outdated-ui');
    expect(overlay).not.toBeNull();
    expect((overlay as HTMLElement).textContent).toContain('Veraltet');
  });

  it('leaves the page alone when the host cannot be reached', async () => {
    stampCommit('abc1234');
    const recorder = deps('http://host/deck', null);

    await runVersionCheck(() => 'http://host', recorder.deps);

    expect(recorder.replaced).toEqual([]);
    expect(recorder.rewritten).toEqual([]);
  });
});

describe('readUiCommit', () => {
  afterEach(() => stampCommit(null));

  it('reads the stamped commit', () => {
    stampCommit(' abc1234 ');
    expect(readUiCommit(document)).toBe('abc1234');
  });

  it('reports nothing for an unstamped document', () => {
    stampCommit(null);
    expect(readUiCommit(document)).toBeNull();
  });
});

describe('showOutdatedUiError', () => {
  afterEach(() => {
    const overlay = document.getElementById('macro-deck-outdated-ui');
    if (overlay && overlay.parentNode) overlay.parentNode.removeChild(overlay);
  });

  it('shows one notice however often it is asked', () => {
    showOutdatedUiError(document, { heading: 'A', body: 'B' });
    showOutdatedUiError(document, { heading: 'C', body: 'D' });

    expect(document.querySelectorAll('#macro-deck-outdated-ui').length).toBe(1);
  });
});
