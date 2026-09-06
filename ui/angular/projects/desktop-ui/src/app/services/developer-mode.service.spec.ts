import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { ApiService } from '@shared';
import { DeveloperModeService } from './developer-mode.service';

describe('DeveloperModeService', () => {
  let service: DeveloperModeService;
  let getStoreStatus: jasmine.Spy;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    getStoreStatus = jasmine.createSpy('getStoreStatus').and.returnValue(Promise.resolve({ developerMode: false }));

    const api = {
      getStoreStatus,
      connectionStateSignal: connectionState,
      onNotification: (method: string) => {
        let subject = notifications.get(method);
        if (!subject) {
          subject = new Subject<unknown>();
          notifications.set(method, subject);
        }
        return subject.asObservable() as Observable<never>;
      },
    };

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: api }],
    });

    service = TestBed.inject(DeveloperModeService);
  });

  // Issue #752: enabling Developer Mode has to take effect without a reload. Caching the answer for the
  // lifetime of the page meant the store went on offering nothing until the user restarted the app.
  it('observes Developer Mode being turned on without a reload', async () => {
    await service.ensureLoaded();
    expect(service.enabled()).toBeFalse();

    getStoreStatus.and.returnValue(Promise.resolve({ developerMode: true }));
    await service.ensureLoaded();

    expect(service.enabled()).toBeTrue();
  });

  it('shares one request between concurrent callers', async () => {
    await Promise.all([service.ensureLoaded(), service.ensureLoaded()]);

    expect(getStoreStatus).toHaveBeenCalledTimes(1);
  });

  // Issue #753: the developer tools have to become usable the moment the toggle is flipped, so the
  // pushed value has to land without anyone asking the host again.
  it('takes the pushed value without a fetch', async () => {
    await service.ensureLoaded();
    getStoreStatus.calls.reset();

    push('DeveloperSettingsChangedEvent', { enabled: true });
    expect(service.enabled()).toBeTrue();

    push('DeveloperSettingsChangedEvent', { enabled: false });
    expect(service.enabled()).toBeFalse();

    expect(getStoreStatus).not.toHaveBeenCalled();
  });

  // The reconnect fetch and the push race each other. Last-writer-wins would let a reconnect that
  // started before the toggle was flipped clobber the pushed value, leaving the tools gated until
  // whenever the next event happens to arrive.
  it('keeps a pushed value that lands while a fetch is in flight', async () => {
    let resolveStatus: (value: { developerMode: boolean }) => void = () => {};
    getStoreStatus.and.returnValue(new Promise<{ developerMode: boolean }>(resolve => {
      resolveStatus = resolve;
    }));

    const pending = service.ensureLoaded();
    push('DeveloperSettingsChangedEvent', { enabled: true });

    resolveStatus({ developerMode: false });
    await pending;

    expect(service.enabled()).toBeTrue();
  });
});
