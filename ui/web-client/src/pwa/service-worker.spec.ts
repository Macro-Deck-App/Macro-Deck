import { readFileSync } from 'fs';
import { join } from 'path';

import { RELOAD_PARAM } from '../version-check';

describe('service worker routing', () => {
  let isForeignRoute: (pathname: string) => boolean;

  beforeAll(() => {
    // The worker is plain JS meant for a worker scope, so the one function under test is lifted out
    // rather than the file being imported - importing it would run its listener registrations.
    const source = readFileSync(join(__dirname, '..', '..', 'src', 'pwa', 'service-worker.js'), 'utf8');
    const segments = /var FOREIGN_SEGMENTS = \[[^\]]*\];/.exec(source);
    const routine = /function isForeignRoute\(pathname\) \{[\s\S]*?\n\}/.exec(source);
    if (segments === null || routine === null) {
      throw new Error('the worker no longer decides which routes are foreign in one place');
    }
    isForeignRoute = new Function(`${segments[0]}\n${routine[0]}\nreturn isForeignRoute;`)() as typeof isForeignRoute;
  });

  it('leaves the configuration UI to the host', () => {
    // Its shell is a different application; answering it with the deck's is ADR 0041's first rule.
    expect(isForeignRoute('/admin')).toBeTrue();
    expect(isForeignRoute('/admin/')).toBeTrue();
    expect(isForeignRoute('/admin/settings')).toBeTrue();
  });

  it('leaves a device target to the host', () => {
    // A target is its own build with its own host endpoint; the default shell cannot
    // stand in for it.
    expect(isForeignRoute('/targets/carthing/')).toBeTrue();
    expect(isForeignRoute('/targets/carthing/index.html')).toBeTrue();
  });

  it('never answers an API call with a shell', () => {
    expect(isForeignRoute('/api/folders')).toBeTrue();
  });

  it('still owns the deck itself', () => {
    expect(isForeignRoute('/')).toBeFalse();
    expect(isForeignRoute('/index.html')).toBeFalse();
    expect(isForeignRoute('/main-ABC123.js')).toBeFalse();
  });

  it('claims a route only on a whole first segment', () => {
    // `/administration` is not the configuration UI, and a prefix match would have taken it.
    expect(isForeignRoute('/administration')).toBeFalse();
    expect(isForeignRoute('/apiary')).toBeFalse();
  });
});

describe('service worker after an update', () => {
  let prefersNetwork: (url: URL) => boolean;
  let workerReloadParam: string;

  beforeAll(() => {
    const source = readFileSync(join(__dirname, '..', '..', 'src', 'pwa', 'service-worker.js'), 'utf8');
    const param = /var RELOAD_PARAM = '([^']*)';/.exec(source);
    const routine = /function prefersNetwork\(url\) \{[\s\S]*?\n\}/.exec(source);
    if (param === null || routine === null) {
      throw new Error('the worker no longer decides in one place which navigations skip its shell');
    }
    workerReloadParam = param[1];
    prefersNetwork = new Function(`${param[0]}\n${routine[0]}\nreturn prefersNetwork;`)() as typeof prefersNetwork;
  });

  it('fetches the reload the version check asks for from the host, not from its own copy', () => {
    expect(prefersNetwork(new URL('https://deck.local/?md-reload=abc1234'))).toBeTrue();
    expect(prefersNetwork(new URL('https://deck.local/?folder=home&md-reload=abc1234'))).toBeTrue();
  });

  it('keeps answering an ordinary navigation from its copy', () => {
    expect(prefersNetwork(new URL('https://deck.local/'))).toBeFalse();
    expect(prefersNetwork(new URL('https://deck.local/?folder=home'))).toBeFalse();
  });

  it('uses the same parameter the version check writes', () => {
    expect(workerReloadParam).toBe(RELOAD_PARAM);
  });
});
