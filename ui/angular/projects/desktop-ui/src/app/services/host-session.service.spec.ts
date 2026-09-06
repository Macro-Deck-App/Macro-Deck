import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject } from 'rxjs';
import { ApiService } from '@shared';
import { HostSessionService } from './host-session.service';

class TestableHostSessionService extends HostSessionService {
  reloads = 0;

  protected override reload(): void {
    this.reloads++;
  }
}

describe('HostSessionService', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let connectionState$: BehaviorSubject<string>;
  let service: TestableHostSessionService;

  beforeEach(() => {
    sessionStorage.clear();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getHostSession']);
    connectionState$ = new BehaviorSubject<string>('disconnected');
    Object.defineProperty(apiSpy, 'connectionState$', { value: connectionState$ });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        TestableHostSessionService,
        { provide: ApiService, useValue: apiSpy },
      ],
    });
    service = TestBed.inject(TestableHostSessionService);
  });

  afterEach(() => sessionStorage.clear());

  async function connect(sessionId: string, restoreApplied = false): Promise<void> {
    apiSpy.getHostSession.and.resolveTo({ sessionId, restoreApplied });
    connectionState$.next('connected');
    await Promise.resolve();
    await Promise.resolve();
  }

  it('does not reload the first time it sees a host', async () => {
    service.start();

    await connect('session-1');

    expect(service.reloads).toBe(0);
  });

  it('does not reload when the same host is still there after a reconnect', async () => {
    service.start();
    await connect('session-1');

    connectionState$.next('disconnected');
    await connect('session-1');

    expect(service.reloads).toBe(0);
  });

  // The case that produced this: after a restore the host comes back with different data, and anything
  // the client cached describes an installation that no longer exists.
  it('reloads once when the host it reconnects to is a different run', async () => {
    service.start();
    await connect('session-1');

    await connect('session-2');

    expect(service.reloads).toBe(1);
  });

  it('does not reload again for the host it just recorded', async () => {
    service.start();
    await connect('session-1');
    await connect('session-2');

    await connect('session-2');

    expect(service.reloads).toBe(1);
  });
});
