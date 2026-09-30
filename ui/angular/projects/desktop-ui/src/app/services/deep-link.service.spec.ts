import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { EMPTY } from 'rxjs';
import { ResolveStoreLinkResponse } from '@macro-deck/runtime';
import { ApiService, ToastService } from '@shared';
import { provideLocalizationTesting } from '../../testing/localization-test-support';
import { DeepLinkService, DEEP_LINK_RETRY_DELAYS_MS } from './deep-link.service';

describe('DeepLinkService', () => {
  let service: DeepLinkService;
  let router: jasmine.SpyObj<Router>;
  let api: jasmine.SpyObj<ApiService>;
  let toasts: jasmine.SpyObj<ToastService>;

  const found: ResolveStoreLinkResponse = { kind: 'IconPack', id: 'com.acme.icons' };
  const notFound: ResolveStoreLinkResponse = { error: { code: 'not_found', message: 'nope' } };
  const unavailable: ResolveStoreLinkResponse = { error: { code: 'registry_unavailable', message: 'wait' } };

  function useShell(bridge: object | null): void {
    if (bridge) {
      (window as { macroDeckShell?: unknown }).macroDeckShell = bridge;
    } else {
      delete (window as { macroDeckShell?: unknown }).macroDeckShell;
    }
  }

  beforeEach(() => {
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
    api = jasmine.createSpyObj<ApiService>('ApiService', ['resolveStoreLink', 'onNotification']);
    api.onNotification.and.returnValue(EMPTY);
    toasts = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: Router, useValue: router },
        { provide: ApiService, useValue: api },
        { provide: ToastService, useValue: toasts },
      ],
    });
    service = TestBed.inject(DeepLinkService);
  });

  afterEach(() => useShell(null));

  it('opens the Store page of the kind and id the host resolved, not the ones in the link', async () => {
    api.resolveStoreLink.and.resolveTo(found);

    await service.open({ kind: 'store', packageId: 'com.acme.icons' });

    expect(api.resolveStoreLink).toHaveBeenCalledWith('com.acme.icons');
    expect(router.navigate).toHaveBeenCalledWith(['/store', 'IconPack', 'com.acme.icons']);
  });

  it('tells the user when the entry is not in the official Store and navigates nowhere', async () => {
    api.resolveStoreLink.and.resolveTo(notFound);

    await service.open({ kind: 'store', packageId: 'com.acme.gone' });

    expect(router.navigate).not.toHaveBeenCalled();
    expect(toasts.show).toHaveBeenCalledWith('This extension could not be found.', { variant: 'error' });
  });

  it('never sends anything but a well-formed package id to the host', async () => {
    for (const packageId of ['../../settings', 'com.acme.hue/reviews', 'COM.ACME.HUE', 'a', 'com.acme.hue?registry=x', '']) {
      await service.open({ kind: 'store', packageId });
    }

    expect(api.resolveStoreLink).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('ignores a destination it does not know', async () => {
    await service.open({ kind: 'settings', packageId: 'com.acme.hue' } as never);

    expect(api.resolveStoreLink).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('refuses a resolved kind that is not a Store kind', async () => {
    api.resolveStoreLink.and.resolveTo({ kind: '../deck' as never, id: 'com.acme.hue' });

    await service.open({ kind: 'store', packageId: 'com.acme.hue' });

    expect(router.navigate).not.toHaveBeenCalled();
  });

  describe('while the registry is still loading', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    async function flush(): Promise<void> {
      for (let turn = 0; turn < 10; turn++) {
        await Promise.resolve();
      }
    }

    it('waits and opens the page once the registry answers', async () => {
      api.resolveStoreLink.and.returnValues(Promise.resolve(unavailable), Promise.resolve(found));

      const opened = service.open({ kind: 'store', packageId: 'com.acme.icons' });
      await flush();
      jasmine.clock().tick(DEEP_LINK_RETRY_DELAYS_MS[0]);
      await opened;

      expect(api.resolveStoreLink).toHaveBeenCalledTimes(2);
      expect(router.navigate).toHaveBeenCalledWith(['/store', 'IconPack', 'com.acme.icons']);
      expect(toasts.show).not.toHaveBeenCalled();
    });

    it('drops a link that is still waiting for the registry when a newer one arrives', async () => {
      api.resolveStoreLink.and.callFake(id => Promise.resolve(id === 'com.acme.first' ? unavailable : found));

      const first = service.open({ kind: 'store', packageId: 'com.acme.first' });
      await flush();
      await service.open({ kind: 'store', packageId: 'com.acme.icons' });
      jasmine.clock().tick(DEEP_LINK_RETRY_DELAYS_MS[0]);
      await first;

      expect(router.navigate).toHaveBeenCalledOnceWith(['/store', 'IconPack', 'com.acme.icons']);
      expect(api.resolveStoreLink).toHaveBeenCalledTimes(2);
    });

    it('gives up with a message when the registry never becomes available', async () => {
      api.resolveStoreLink.and.resolveTo(unavailable);

      const opened = service.open({ kind: 'store', packageId: 'com.acme.icons' });
      for (const delay of DEEP_LINK_RETRY_DELAYS_MS) {
        await flush();
        jasmine.clock().tick(delay);
      }
      await opened;

      expect(api.resolveStoreLink).toHaveBeenCalledTimes(DEEP_LINK_RETRY_DELAYS_MS.length + 1);
      expect(router.navigate).not.toHaveBeenCalled();
      expect(toasts.show).toHaveBeenCalledOnceWith(
        'The Store is not available right now. Check your internet connection and open the link again.',
        { variant: 'error' });
    });
  });

  it('opens a link that was queued before it started listening, and only the most recent one', async () => {
    api.resolveStoreLink.and.resolveTo(found);
    const takeDeepLinks = jasmine.createSpy('takeDeepLinks').and.resolveTo([
      { kind: 'store', packageId: 'com.acme.first' },
      { kind: 'store', packageId: 'com.acme.icons' },
    ]);
    useShell({ onDeepLink: () => Promise.resolve(() => undefined), takeDeepLinks });

    service.start();
    await new Promise(resolve => setTimeout(resolve));

    expect(api.resolveStoreLink).toHaveBeenCalledOnceWith('com.acme.icons');
  });

  it('opens a link announced while the app is running', async () => {
    api.resolveStoreLink.and.resolveTo(found);
    let announce: () => void = () => undefined;
    const takeDeepLinks = jasmine.createSpy('takeDeepLinks').and.resolveTo([]);
    useShell({
      onDeepLink: (callback: () => void) => {
        announce = callback;
        return Promise.resolve(() => undefined);
      },
      takeDeepLinks,
    });
    service.start();
    await new Promise(resolve => setTimeout(resolve));
    takeDeepLinks.and.resolveTo([{ kind: 'store', packageId: 'com.acme.icons' }]);

    announce();
    await new Promise(resolve => setTimeout(resolve));

    expect(router.navigate).toHaveBeenCalledWith(['/store', 'IconPack', 'com.acme.icons']);
  });

  it('does nothing outside the desktop shell', () => {
    useShell(null);

    expect(() => service.start()).not.toThrow();
    expect(api.resolveStoreLink).not.toHaveBeenCalled();
  });
});
