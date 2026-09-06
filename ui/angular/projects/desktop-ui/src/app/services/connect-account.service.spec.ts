import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { GetConnectSessionResponse, StartConnectSignInResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ConnectAccountService } from './connect-account.service';
import { ExternalLinkService } from './external-link.service';

function session(overrides: Partial<GetConnectSessionResponse> = {}): GetConnectSessionResponse {
  return {
    status: 'signedOut',
    connectivity: 'ok',
    offlineSince: null,
    account: null,
    lastSuccessfulRefreshUtc: null,
    message: null,
    signInFailure: null,
    accountManagementUrl: 'https://accounts.macro-deck.app/',
    ...overrides,
  };
}

function pendingSignIn(): StartConnectSignInResponse {
  return {
    verificationUriComplete: 'https://accounts.macro-deck.app/device?user_code=3389-5291',
    verificationUri: 'https://accounts.macro-deck.app/device',
    userCode: '3389-5291',
    expiresAtUtc: '2026-08-18T01:00:00Z',
  };
}

describe('ConnectAccountService', () => {
  let service: ConnectAccountService;
  let api: jasmine.SpyObj<ApiService>;
  let externalLink: jasmine.SpyObj<ExternalLinkService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;

  function push(method: string, payload: unknown = {}): void {
    notifications.get(method)?.next(payload);
  }

  beforeEach(() => {
    connectionState = signal<string>('disconnected');
    notifications = new Map();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'onConnectSessionChanged', 'getConnectSession', 'startConnectSignIn', 'cancelConnectSignIn',
      'signOutConnect', 'getConnectAvatarUrl',
    ]);
    function notificationsFor(method: string): Observable<never> {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<never>;
    }
    api.onNotification.and.callFake(notificationsFor);
    api.onConnectSessionChanged.and.callFake(() => notificationsFor('ConnectSessionChangedNotification'));
    api.getConnectSession.and.resolveTo(session());
    Object.defineProperty(api, 'connectionStateSignal', { value: connectionState });

    externalLink = jasmine.createSpyObj<ExternalLinkService>('ExternalLinkService', ['open']);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ExternalLinkService, useValue: externalLink },
      ],
    });
    service = TestBed.inject(ConnectAccountService);
  });

  it('re-fetches the session when the change notification arrives', async () => {
    connectionState.set('connected');
    await service.load();
    // The connected-state effect kicks off a refresh of its own; `load()` is single-flight, so awaiting
    // it again settles that one instead of racing the notification against a still-pending fetch.
    TestBed.tick();
    await service.load();
    expect(service.session()?.status).toBe('signedOut');

    api.getConnectSession.and.resolveTo(session({ status: 'signedIn', account: {
      subject: 'u1', displayName: 'Jane Doe', avatarAvailable: false, avatarVersion: null, creatorUsername: null, roles: [],
    } }));
    const callsBefore = api.getConnectSession.calls.count();

    push('ConnectSessionChangedNotification');
    await Promise.resolve();
    await Promise.resolve();

    expect(api.getConnectSession.calls.count()).toBe(callsBefore + 1);
    expect(service.session()?.status).toBe('signedIn');
  });

  it('opens the confirmation page that already carries the code, so nothing has to be typed', async () => {
    api.startConnectSignIn.and.resolveTo(pendingSignIn());
    const originalLocation = window.location.href;

    await service.signIn();

    expect(externalLink.open)
      .toHaveBeenCalledOnceWith('https://accounts.macro-deck.app/device?user_code=3389-5291');
    expect(window.location.href).toBe(originalLocation);
  });

  it('keeps the pending code available while the host reports signing in', async () => {
    api.startConnectSignIn.and.resolveTo(pendingSignIn());
    api.getConnectSession.and.resolveTo(session({ status: 'signingIn' }));
    await service.load();

    await service.signIn();
    await service.load();

    expect(service.signInPrompt()?.userCode).toBe('3389-5291');
  });

  it('drops the pending code as soon as the attempt is no longer running', async () => {
    api.startConnectSignIn.and.resolveTo(pendingSignIn());
    api.getConnectSession.and.resolveTo(session({ status: 'signingIn' }));
    await service.load();
    await service.signIn();

    // An expired or declined attempt ends as signedOut; a stale code on screen would invite the user to
    // confirm something the host is no longer waiting for.
    api.getConnectSession.and.resolveTo(session({ status: 'signedOut', signInFailure: 'expired' }));
    await service.load();

    expect(service.signInPrompt()).toBeNull();
  });

  it('exposes a status label and signed-in/needs-attention flags derived from the session', async () => {
    api.getConnectSession.and.resolveTo(session({ status: 'suspended', message: 'Payment required.' }));
    connectionState.set('connected');
    await service.load();
    TestBed.tick();

    expect(service.isSignedIn()).toBeFalse();
    expect(service.needsAttention()).toBeTrue();
    expect(service.statusLabel()).toContain('suspended');
  });
});
