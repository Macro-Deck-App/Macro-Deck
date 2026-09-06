import { shouldRegisterServiceWorker } from './service-worker-registration';

function capableEnvironment(): { isSecureContext: boolean; navigator: { serviceWorker?: unknown } } {
  return { isSecureContext: true, navigator: { serviceWorker: {} } };
}

describe('shouldRegisterServiceWorker', () => {
  it('registers on a secure origin in a capable browser', () => {
    expect(shouldRegisterServiceWorker(capableEnvironment(), false)).toBe(true);
  });

  it('refuses on an insecure origin', () => {
    // The LAN client is normally served over plain HTTP; the browser would reject the registration,
    // and offering install there would be a promise the address cannot keep.
    const environment = { isSecureContext: false, navigator: { serviceWorker: {} } };

    expect(shouldRegisterServiceWorker(environment, false)).toBe(false);
  });

  it('refuses when the browser has no service worker support', () => {
    // Every browser now loads the same down-levelled bundle, so the engines below the floor reach
    // this code path rather than a shell of their own; this check is what turns them away.
    expect(shouldRegisterServiceWorker({ isSecureContext: true, navigator: {} }, false)).toBe(false);
  });

  it('refuses in development, where the served shell is unoptimised', () => {
    expect(shouldRegisterServiceWorker(capableEnvironment(), true)).toBe(false);
  });
});
