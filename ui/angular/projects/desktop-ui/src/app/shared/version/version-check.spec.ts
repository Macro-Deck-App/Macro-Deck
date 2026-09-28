import { ApplicationInitStatus, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AppStrings, Strings } from '@macro-deck/runtime';
import { Subject } from 'rxjs';

import { ConnectionState, HOST_URL_RESOLVER } from '../transport';
import {
  RELOAD_PARAM,
  UI_COMMIT_META,
  OutdatedUiSetup,
  VersionCheckDeps,
  connectionEstablished,
  createVersionCheckRunner,
  decideVersionAction,
  outdatedUiText,
  provideVersionCheck,
  runVersionCheck,
  withCacheBust,
} from './version-check';

describe('decideVersionAction', () => {
  it('accepts a matching commit regardless of case', () => {
    expect(decideVersionAction('abc1234', 'ABC1234', [null, null])).toBe('ok');
  });

  it('accepts a host without commit metadata', () => {
    expect(decideVersionAction('abc1234', null, [null, null])).toBe('ok');
  });

  it('reloads on the first mismatch and reports once a reload did not help', () => {
    expect(decideVersionAction('abc1234', 'def5678', [null, null])).toBe('reload');
    expect(decideVersionAction('abc1234', 'def5678', ['def5678', null])).toBe('report');
  });

  it('treats the url parameter as an attempt too, for storage-less browsers', () => {
    expect(decideVersionAction('abc1234', 'def5678', [null, 'def5678'])).toBe('report');
  });

  it('reloads again when the host moved on to yet another commit', () => {
    expect(decideVersionAction('abc1234', 'aaa1111', ['def5678', 'def5678'])).toBe('reload');
  });
});

describe('withCacheBust', () => {
  it('adds the parameter before the hash route and replaces an earlier one', () => {
    expect(withCacheBust('http://localhost/admin/index.html#/profiles', 'abc1234'))
      .toBe(`http://localhost/admin/index.html?${RELOAD_PARAM}=abc1234#/profiles`);
    expect(withCacheBust(`http://localhost/?${RELOAD_PARAM}=old`, 'abc1234'))
      .toBe(`http://localhost/?${RELOAD_PARAM}=abc1234`);
  });
});

describe('runVersionCheck', () => {
  let doc: Document;
  let store: Record<string, string>;
  let deps: VersionCheckDeps;
  let url: string;

  function stampUiCommit(commit: string): void {
    const meta = doc.createElement('meta');
    meta.name = UI_COMMIT_META;
    meta.content = commit;
    doc.head.append(meta);
  }

  beforeEach(() => {
    doc = document.implementation.createHTMLDocument('test');
    store = {};
    url = 'http://localhost/admin/index.html#/profiles';
    deps = {
      doc,
      storage: {
        getItem: (key) => store[key] ?? null,
        setItem: (key, value) => (store[key] = value),
        removeItem: (key) => delete store[key],
      },
      currentUrl: () => url,
      replaceUrl: jasmine.createSpy('replaceUrl'),
      replaceUrlInPlace: jasmine.createSpy('replaceUrlInPlace'),
      fetchHostCommit: jasmine.createSpy('fetchHostCommit').and.resolveTo('def5678'),
      fetchServedCommit: jasmine.createSpy('fetchServedCommit').and.resolveTo('def5678'),
      now: () => 1700000000000,
      outdated: outdatedSetup,
    };
    refreshedFor = [];
  });

  let refreshedFor: string[] = [];
  const outdatedSetup: OutdatedUiSetup = {
    text: (variant, device, computer) => ({
      title: `${variant} title`,
      body: 'body',
      steps: ['step'],
      action: 'action',
      versions: `${device} / ${computer}`,
    }),
    refresh: (hostCommit) => refreshedFor.push(hostCommit),
  };

  function shownTitle(): string | null | undefined {
    return doc.getElementById('macro-deck-outdated-ui')?.querySelector('h1')?.textContent;
  }

  it('does nothing when the bundle carries no commit', async () => {
    await runVersionCheck(async () => 'http://localhost', deps);

    expect(deps.fetchHostCommit).not.toHaveBeenCalled();
    expect(deps.replaceUrl).not.toHaveBeenCalled();
  });

  it('does nothing when the host url cannot be resolved', async () => {
    stampUiCommit('abc1234');

    await runVersionCheck(async () => null, deps);

    expect(deps.fetchHostCommit).not.toHaveBeenCalled();
  });

  it('reloads once with a cache-busted url and remembers the attempt', async () => {
    stampUiCommit('abc1234');

    await runVersionCheck(async () => 'http://localhost', deps);

    expect(deps.replaceUrl).toHaveBeenCalledOnceWith(
      `http://localhost/admin/index.html?${RELOAD_PARAM}=def5678#/profiles`
    );
    expect(store['macro-deck.reloaded-for']).toBe('def5678');
  });

  it('shows a blocking error instead of reloading again', async () => {
    stampUiCommit('abc1234');
    store['macro-deck.reloaded-for'] = 'def5678';
    spyOn(console, 'error');

    await runVersionCheck(async () => 'http://localhost', deps);

    expect(deps.replaceUrl).not.toHaveBeenCalled();
    expect(shownTitle()).toBe('device title');
    doc.querySelector<HTMLButtonElement>('#macro-deck-outdated-ui button')!.click();
    expect(refreshedFor).toEqual(['def5678']);
  });

  it('blames the installation when the host serves a different interface than it runs', async () => {
    stampUiCommit('abc1234');
    store['macro-deck.reloaded-for'] = 'def5678';
    spyOn(console, 'error');
    deps.fetchServedCommit = jasmine.createSpy('fetchServedCommit').and.resolveTo('abc1234');

    await runVersionCheck(async () => 'http://localhost', deps);

    expect(shownTitle()).toBe('installation title');
    expect(doc.getElementById('macro-deck-outdated-ui')!.textContent).toContain('abc1234 / def5678');
  });

  it('keeps the error up when a later check cannot reach the host', async () => {
    stampUiCommit('abc1234');
    store['macro-deck.reloaded-for'] = 'def5678';
    spyOn(console, 'error');
    await runVersionCheck(async () => 'http://localhost', deps);

    deps.fetchHostCommit = jasmine.createSpy('fetchHostCommit').and.resolveTo(null);
    await runVersionCheck(async () => 'http://localhost', deps);

    expect(shownTitle()).toBe('device title');
    expect(store['macro-deck.reloaded-for']).toBe('def5678');
  });

  it('refreshes by itself once the installation serves what it reports', async () => {
    stampUiCommit('abc1234');
    store['macro-deck.reloaded-for'] = 'def5678';
    spyOn(console, 'error');
    deps.fetchServedCommit = jasmine.createSpy('fetchServedCommit').and.resolveTo('abc1234');
    await runVersionCheck(async () => 'http://localhost', deps);

    deps.fetchServedCommit = jasmine.createSpy('fetchServedCommit').and.resolveTo('def5678');
    await runVersionCheck(async () => 'http://localhost', deps);

    expect(refreshedFor).toEqual(['def5678']);
  });

  it('takes the error down once the interface and the host agree again', async () => {
    stampUiCommit('abc1234');
    store['macro-deck.reloaded-for'] = 'def5678';
    spyOn(console, 'error');
    await runVersionCheck(async () => 'http://localhost', deps);

    deps.fetchHostCommit = jasmine.createSpy('fetchHostCommit').and.resolveTo('abc1234');
    await runVersionCheck(async () => 'http://localhost', deps);

    expect(shownTitle()).toBeUndefined();
  });

  it('renders the error only once', async () => {
    stampUiCommit('abc1234');
    store['macro-deck.reloaded-for'] = 'def5678';
    spyOn(console, 'error');

    await runVersionCheck(async () => 'http://localhost', deps);
    await runVersionCheck(async () => 'http://localhost', deps);

    expect(doc.querySelectorAll('#macro-deck-outdated-ui').length).toBe(1);
  });

  it('clears the marker and the reload parameter once the commits match', async () => {
    stampUiCommit('def5678');
    store['macro-deck.reloaded-for'] = 'def5678';
    url = `http://localhost/admin/index.html?${RELOAD_PARAM}=def5678#/profiles`;

    await runVersionCheck(async () => 'http://localhost', deps);

    expect(store['macro-deck.reloaded-for']).toBeUndefined();
    expect(deps.replaceUrlInPlace).toHaveBeenCalledOnceWith(
      'http://localhost/admin/index.html#/profiles'
    );
    expect(deps.replaceUrl).not.toHaveBeenCalled();
  });

  it('reports instead of reloading when the url already carries the attempt', async () => {
    stampUiCommit('abc1234');
    url = `http://localhost/admin/index.html?${RELOAD_PARAM}=def5678#/profiles`;
    spyOn(console, 'error');

    await runVersionCheck(async () => 'http://localhost', deps);

    expect(deps.replaceUrl).not.toHaveBeenCalled();
    expect(doc.getElementById('macro-deck-outdated-ui')).not.toBeNull();
  });

  it('probes the host with a cache-busting nonce', async () => {
    stampUiCommit('abc1234');

    await runVersionCheck(async () => 'http://localhost', deps);

    expect(deps.fetchHostCommit).toHaveBeenCalledOnceWith('http://localhost', 'abc1234-1700000000000');
  });

  it('leaves the page alone when the host reports no commit', async () => {
    stampUiCommit('abc1234');
    (deps.fetchHostCommit as jasmine.Spy).and.resolveTo(null);

    await runVersionCheck(async () => 'http://localhost', deps);

    expect(deps.replaceUrl).not.toHaveBeenCalled();
    expect(deps.replaceUrlInPlace).not.toHaveBeenCalled();
  });

  describe('prepareReload hook', () => {
    it('is awaited before the reload navigates away', async () => {
      stampUiCommit('abc1234');
      const order: string[] = [];
      deps.prepareReload = () => {
        order.push('prepare');
        return Promise.resolve();
      };
      (deps.replaceUrl as jasmine.Spy).and.callFake(() => order.push('reload'));

      await runVersionCheck(async () => 'http://localhost', deps);

      expect(order).toEqual(['prepare', 'reload']);
    });

    it('still reloads exactly once when the hook rejects', async () => {
      stampUiCommit('abc1234');
      deps.prepareReload = () => Promise.reject(new Error('boom'));

      await runVersionCheck(async () => 'http://localhost', deps);

      expect(deps.replaceUrl).toHaveBeenCalledTimes(1);
    });

    it('is not called on the healthy path', async () => {
      stampUiCommit('def5678');
      const prepareReload = jasmine.createSpy('prepareReload').and.resolveTo(undefined);
      deps.prepareReload = prepareReload;

      await runVersionCheck(async () => 'http://localhost', deps);

      expect(prepareReload).not.toHaveBeenCalled();
      expect(deps.replaceUrl).not.toHaveBeenCalled();
    });

    it('is not called on the already-reported path', async () => {
      stampUiCommit('abc1234');
      store['macro-deck.reloaded-for'] = 'def5678';
      spyOn(console, 'error');
      const prepareReload = jasmine.createSpy('prepareReload').and.resolveTo(undefined);
      deps.prepareReload = prepareReload;

      await runVersionCheck(async () => 'http://localhost', deps);

      expect(prepareReload).not.toHaveBeenCalled();
      expect(deps.replaceUrl).not.toHaveBeenCalled();
    });
  });
});

describe('outdatedUiText', () => {
  const localization = {
    translateKey: (key: string, args?: Record<string, unknown>) => (args ? `${key} ${JSON.stringify(args)}` : key),
  };

  it('speaks about this window when only the interface is stale', () => {
    const text = outdatedUiText(localization, 'device', 'abc1234', 'def5678');

    expect(text.title).toBe(AppStrings.Shell.OutdatedUi.Device.Title);
    expect(text.action).toBe(Strings.Common.Refresh);
    expect(text.versions).toContain('"device":"abc1234"');
    expect(text.versions).toContain('"computer":"def5678"');
  });

  it('asks for a reinstall when the installation does not fit together', () => {
    const text = outdatedUiText(localization, 'installation', 'abc1234', 'def5678');

    expect(text.title).toBe(AppStrings.OutdatedUi.Installation.Title);
    expect(text.steps).toEqual([
      AppStrings.Shell.OutdatedUi.Installation.Step.Reinstall,
      AppStrings.OutdatedUi.Installation.Step.Antivirus,
      AppStrings.OutdatedUi.Installation.Step.Retry,
    ]);
    expect(text.action).toBe(Strings.Common.Retry);
  });
});

describe('connectionEstablished', () => {
  it('emits once per transition into a live connection', () => {
    const state$ = new Subject<ConnectionState>();
    let emissions = 0;
    connectionEstablished(state$).subscribe(() => emissions++);

    for (const state of [
      'disconnected', 'connecting', 'connected', 'connected', 'connecting', 'connected',
    ] as const) {
      state$.next(state);
    }

    expect(emissions).toBe(2);
  });
});

describe('createVersionCheckRunner', () => {
  let deps: VersionCheckDeps;
  let hostCommit: (commit: string | null) => void = () => undefined;

  function stampUiCommit(commit: string): void {
    const meta = deps.doc.createElement('meta');
    meta.name = UI_COMMIT_META;
    meta.content = commit;
    deps.doc.head.append(meta);
  }

  beforeEach(() => {
    deps = {
      doc: document.implementation.createHTMLDocument('test'),
      storage: { getItem: () => null, setItem: () => undefined, removeItem: () => undefined },
      currentUrl: () => 'http://localhost/admin/index.html',
      replaceUrl: jasmine.createSpy('replaceUrl'),
      replaceUrlInPlace: jasmine.createSpy('replaceUrlInPlace'),
      fetchHostCommit: jasmine.createSpy('fetchHostCommit').and.callFake(
        () => new Promise<string | null>(resolve => (hostCommit = resolve))
      ),
      fetchServedCommit: async () => null,
      now: () => 1700000000000,
    };
    stampUiCommit('abc1234');
  });

  it('drops a trigger arriving while a probe is still in flight', async () => {
    const check = createVersionCheckRunner(async () => 'http://localhost', deps);

    check();
    check();
    await flushMicrotasks();

    expect(deps.fetchHostCommit).toHaveBeenCalledTimes(1);

    hostCommit('abc1234');
    await flushMicrotasks();
    check();
    await flushMicrotasks();

    expect(deps.fetchHostCommit).toHaveBeenCalledTimes(2);
  });

  it('stays usable after a run that could not reach the host', async () => {
    let reachable = false;
    const check = createVersionCheckRunner(async () => {
      if (!reachable) {
        throw new Error('host unreachable');
      }
      return 'http://localhost';
    }, deps);

    check();
    await flushMicrotasks();
    expect(deps.fetchHostCommit).not.toHaveBeenCalled();

    reachable = true;
    check();
    await flushMicrotasks();

    expect(deps.fetchHostCommit).toHaveBeenCalledTimes(1);
  });
});

async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
  }
}

describe('provideVersionCheck', () => {
  it('skips the check in development builds', async () => {
    const fetchSpy = spyOn(window, 'fetch');
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://localhost' },
        provideVersionCheck(),
      ],
    });

    await TestBed.inject(ApplicationInitStatus).donePromise;

    expect(fetchSpy).not.toHaveBeenCalled();
  });
});
