import { currentOutdatedUi, type OutdatedUiVariant } from '@macro-deck/runtime';

import {
  decideVersionAction,
  hardRefresh,
  readUiCommit,
  runVersionCheck,
  UI_COMMIT_META,
  withCacheBust,
  withoutCacheBust,
  type HardRefreshEnvironment,
  type OutdatedUiSetup,
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
    fetchServedCommit: () => Promise.resolve(hostCommit),
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

function outdatedSetup(refreshed: string[]): OutdatedUiSetup {
  return {
    text: (variant: OutdatedUiVariant, device: string, computer: string) => ({
      title: variant + ' title',
      body: 'body',
      steps: ['step'],
      action: variant + ' action',
      versions: device + ' / ' + computer,
    }),
    hardRefresh: hostCommit => {
      refreshed.push('hard ' + hostCommit);
    },
    reload: hostCommit => {
      refreshed.push('reload ' + hostCommit);
    },
  };
}

function shownTitle(): string | null {
  const overlay = document.getElementById('macro-deck-outdated-ui');
  return overlay ? overlay.querySelector('h1')!.textContent : null;
}

describe('the notice when a reload did not bring the page up to date', () => {
  beforeEach(() => spyOn(console, 'error'));

  afterEach(() => {
    const shown = currentOutdatedUi(document);
    if (shown) shown.remove();
    stampCommit(null);
  });

  function alreadyReloaded(): Pick<Storage, 'getItem' | 'setItem' | 'removeItem'> {
    return memoryStorage({ 'macro-deck.reloaded-for': 'def5678' });
  }

  it('blames this device when the host serves the build it reports, and its button refreshes for that build', async () => {
    stampCommit('abc1234');
    const refreshed: string[] = [];
    const recorder = deps('http://host/deck', 'def5678', alreadyReloaded(), {
      outdated: outdatedSetup(refreshed),
      fetchServedCommit: () => Promise.resolve('DEF5678'),
    });

    await runVersionCheck(() => 'http://host', recorder.deps);

    expect(shownTitle()).toBe('device title');
    expect(document.getElementById('macro-deck-outdated-ui')!.textContent).toContain('abc1234 / def5678');
    document.querySelector<HTMLButtonElement>('#macro-deck-outdated-ui button')!.click();
    expect(refreshed).toEqual(['hard def5678']);
    expect(recorder.replaced).toEqual([]);
  });

  it('blames the installation when the host serves a different build than it reports', async () => {
    stampCommit('abc1234');
    const recorder = deps('http://host/deck', 'def5678', alreadyReloaded(), {
      outdated: outdatedSetup([]),
      fetchServedCommit: () => Promise.resolve('abc1234'),
    });

    await runVersionCheck(() => 'http://host', recorder.deps);

    expect(shownTitle()).toBe('installation title');
  });

  it('only reloads from the installation notice, since this device holds nothing worth dropping', async () => {
    stampCommit('abc1234');
    const refreshed: string[] = [];
    const recorder = deps('http://host/deck', 'def5678', alreadyReloaded(), {
      outdated: outdatedSetup(refreshed),
      fetchServedCommit: () => Promise.resolve('abc1234'),
    });

    await runVersionCheck(() => 'http://host', recorder.deps);
    document.querySelector<HTMLButtonElement>('#macro-deck-outdated-ui button')!.click();

    expect(refreshed).toEqual(['reload def5678']);
  });

  it('loads the repaired build by itself once the installation serves what it reports', async () => {
    stampCommit('abc1234');
    const refreshed: string[] = [];
    await runVersionCheck(() => 'http://host', deps('http://host/deck', 'def5678', alreadyReloaded(), {
      outdated: outdatedSetup(refreshed),
      fetchServedCommit: () => Promise.resolve('abc1234'),
    }).deps);

    await runVersionCheck(() => 'http://host', deps('http://host/deck', 'def5678', alreadyReloaded(), {
      outdated: outdatedSetup(refreshed),
      fetchServedCommit: () => Promise.resolve('def5678'),
    }).deps);

    expect(refreshed).toEqual(['hard def5678']);
  });

  it('falls back to this device when the served page cannot be read', async () => {
    stampCommit('abc1234');
    const recorder = deps('http://host/deck', 'def5678', alreadyReloaded(), {
      outdated: outdatedSetup([]),
      fetchServedCommit: () => Promise.resolve(null),
    });

    await runVersionCheck(() => 'http://host', recorder.deps);

    expect(shownTitle()).toBe('device title');
  });

  it('stays up, and keeps its markers, when a later check cannot reach the host', async () => {
    stampCommit('abc1234');
    const storage = alreadyReloaded();
    const url = 'http://host/deck?md-reload=def5678';
    await runVersionCheck(() => 'http://host', deps(url, 'def5678', storage, { outdated: outdatedSetup([]) }).deps);

    const unreachable = deps(url, null, storage, { outdated: outdatedSetup([]) });
    await runVersionCheck(() => 'http://host', unreachable.deps);

    expect(shownTitle()).toBe('device title');
    expect(storage.getItem('macro-deck.reloaded-for')).toBe('def5678');
    expect(unreachable.rewritten).toEqual([]);
  });

  it('goes away once the page and the host agree again', async () => {
    stampCommit('abc1234');
    await runVersionCheck(
      () => 'http://host',
      deps('http://host/deck', 'def5678', alreadyReloaded(), { outdated: outdatedSetup([]) }).deps);

    await runVersionCheck(
      () => 'http://host',
      deps('http://host/deck', 'abc1234', alreadyReloaded(), { outdated: outdatedSetup([]) }).deps);

    expect(shownTitle()).toBeNull();
  });
});

interface FakeWorkerSetup {
  reachable?: boolean;
  controlled: boolean;
  scriptURL: string;
  cacheNames: string[];
}

function hardRefreshEnvironment(setup: FakeWorkerSetup): {
  env: HardRefreshEnvironment;
  unregistered: () => boolean;
  deleted: string[];
  replaced: string[];
} {
  let unregistered = false;
  const deleted: string[] = [];
  const replaced: string[] = [];
  const registration = {
    active: { scriptURL: setup.scriptURL },
    waiting: null,
    installing: null,
    unregister: () => {
      unregistered = true;
      return Promise.resolve(true);
    },
  };
  const container = {
    controller: setup.controlled ? {} : null,
    getRegistration: () => Promise.resolve(registration),
  } as unknown as ServiceWorkerContainer;
  const storage = {
    keys: () => Promise.resolve(setup.cacheNames.slice()),
    delete: (name: string) => {
      deleted.push(name);
      return Promise.resolve(true);
    },
  } as unknown as CacheStorage;
  return {
    env: {
      serviceWorker: container,
      caches: storage,
      baseUri: 'https://host/',
      hostReachable: () => Promise.resolve(setup.reachable !== false),
      currentUrl: () => 'https://host/?md-reload=def5678',
      replaceUrl: url => {
        replaced.push(url);
      },
    },
    unregistered: () => unregistered,
    deleted,
    replaced,
  };
}

describe('hardRefresh', () => {
  it('drops this client\'s worker and shell caches, then loads the host\'s build', async () => {
    const fake = hardRefreshEnvironment({
      controlled: true,
      scriptURL: 'https://host/macro-deck-worker.js',
      cacheNames: ['macro-deck-shell-v1', 'someone-else'],
    });

    await hardRefresh('def5678', fake.env);

    expect(fake.unregistered()).toBeTrue();
    expect(fake.deleted).toEqual(['macro-deck-shell-v1']);
    expect(fake.replaced).toEqual(['https://host/?md-reload=def5678']);
  });

  it('keeps the worker and its shell while the host cannot be reached', async () => {
    const fake = hardRefreshEnvironment({
      reachable: false,
      controlled: true,
      scriptURL: 'https://host/macro-deck-worker.js',
      cacheNames: ['macro-deck-shell-v1'],
    });

    await hardRefresh('def5678', fake.env);

    expect(fake.unregistered()).toBeFalse();
    expect(fake.deleted).toEqual([]);
    expect(fake.replaced).toEqual(['https://host/?md-reload=def5678']);
  });

  it('leaves a worker that belongs to another app on the same host alone', async () => {
    const fake = hardRefreshEnvironment({
      controlled: true,
      scriptURL: 'https://host/other/worker.js',
      cacheNames: [],
    });

    await hardRefresh('def5678', fake.env);

    expect(fake.unregistered()).toBeFalse();
    expect(fake.replaced.length).toBe(1);
  });

  it('does not touch the worker when it does not control this page', async () => {
    const fake = hardRefreshEnvironment({
      controlled: false,
      scriptURL: 'https://host/macro-deck-worker.js',
      cacheNames: [],
    });

    await hardRefresh('def5678', fake.env);

    expect(fake.unregistered()).toBeFalse();
    expect(fake.replaced.length).toBe(1);
  });

  it('still reloads when the browser never finishes cleaning up', async () => {
    jasmine.clock().install();
    try {
      const replaced: string[] = [];
      const pending = hardRefresh('def5678', {
        serviceWorker: {
          controller: {},
          getRegistration: () => new Promise(() => undefined),
        } as unknown as ServiceWorkerContainer,
        caches: null,
        baseUri: 'https://host/',
        hostReachable: () => Promise.resolve(true),
        currentUrl: () => 'https://host/',
        replaceUrl: url => {
          replaced.push(url);
        },
      });
      jasmine.clock().tick(3000);
      await pending;
      expect(replaced).toEqual(['https://host/?md-reload=def5678']);
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('works without any service worker support', async () => {
    const replaced: string[] = [];
    await hardRefresh('def5678', {
      serviceWorker: null,
      caches: null,
      baseUri: 'https://host/',
      hostReachable: () => Promise.resolve(true),
      currentUrl: () => 'https://host/',
      replaceUrl: url => {
        replaced.push(url);
      },
    });
    expect(replaced).toEqual(['https://host/?md-reload=def5678']);
  });
});
