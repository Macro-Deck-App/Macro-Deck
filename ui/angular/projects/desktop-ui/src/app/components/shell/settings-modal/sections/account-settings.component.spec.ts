import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { GetConnectSessionResponse, StartConnectSignInResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ConnectAccountService } from '../../../../services/connect-account.service';
import { ExternalLinkService } from '../../../../services/external-link.service';
import { AccountSettingsComponent } from './account-settings.component';

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

const MODAL_EXIT_MS = 250;

describe('AccountSettingsComponent', () => {
  let fixture: ComponentFixture<AccountSettingsComponent>;
  let connectSpy: jasmine.SpyObj<ConnectAccountService>;
  let externalLinkSpy: jasmine.SpyObj<ExternalLinkService>;
  let sessionSignal: WritableSignal<GetConnectSessionResponse | null>;
  let promptSignal: WritableSignal<StartConnectSignInResponse | null>;

  function configure(initial: GetConnectSessionResponse): void {
    connectSpy = jasmine.createSpyObj<ConnectAccountService>(
      'ConnectAccountService', ['load', 'signIn', 'cancelSignIn', 'signOut']);
    sessionSignal = signal<GetConnectSessionResponse | null>(initial);
    Object.defineProperty(connectSpy, 'session', { value: sessionSignal });
    Object.defineProperty(connectSpy, 'isLoading', { value: signal(false) });
    Object.defineProperty(connectSpy, 'loadError', { value: signal<string | null>(null) });
    Object.defineProperty(connectSpy, 'avatarUrl', { value: signal<string | null>(null) });
    Object.defineProperty(connectSpy, 'statusLabel', { value: signal('') });
    Object.defineProperty(connectSpy, 'isSignedIn', { value: signal(initial.status === 'signedIn') });
    Object.defineProperty(connectSpy, 'needsAttention', {
      value: signal(initial.status === 'suspended' || initial.status === 'reauthenticationRequired'),
    });
    promptSignal = signal<StartConnectSignInResponse | null>(null);
    Object.defineProperty(connectSpy, 'signInPrompt', { value: promptSignal });
    connectSpy.load.and.resolveTo();
    connectSpy.signIn.and.resolveTo();
    connectSpy.cancelSignIn.and.resolveTo();
    connectSpy.signOut.and.resolveTo();

    externalLinkSpy = jasmine.createSpyObj<ExternalLinkService>('ExternalLinkService', ['open']);

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
    apiSpy.onNotification.and.returnValue(EMPTY);

    TestBed.configureTestingModule({
      imports: [AccountSettingsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ConnectAccountService, useValue: connectSpy },
        { provide: ExternalLinkService, useValue: externalLinkSpy },
        { provide: ApiService, useValue: apiSpy },
      ],
    });
  }

  async function create(): Promise<ComponentFixture<AccountSettingsComponent>> {
    const f = TestBed.createComponent(AccountSettingsComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  function findButtonByText(root: HTMLElement, text: string): HTMLButtonElement | null {
    const buttons = Array.from(root.querySelectorAll('button')) as HTMLButtonElement[];
    return buttons.find(b => b.textContent?.trim() === text) ?? null;
  }

  it('shows a sign-in action under the account heading when signed out', async () => {
    configure(session({ status: 'signedOut' }));
    fixture = await create();

    const signIn = findButtonByText(fixture.nativeElement, 'Sign in');
    expect(signIn).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Macro Deck account');
    expect(fixture.nativeElement.querySelector('img')).toBeNull();

    signIn!.click();
    await fixture.whenStable();

    expect(connectSpy.signIn).toHaveBeenCalledTimes(1);
  });

  it('shows name, status, manage and sign-out when signed in', async () => {
    configure(session({
      status: 'signedIn',
      account: { subject: 'u1', displayName: 'Jane Doe', avatarAvailable: false, avatarVersion: null, creatorUsername: null, roles: [] },
    }));
    fixture = await create();

    expect(fixture.nativeElement.textContent).toContain('Jane Doe');
    expect(findButtonByText(fixture.nativeElement, 'Sign in')).toBeNull();

    const manage = findButtonByText(fixture.nativeElement, 'Manage account');
    expect(manage).toBeTruthy();
    manage!.click();

    expect(externalLinkSpy.open).toHaveBeenCalledOnceWith('https://accounts.macro-deck.app/');

    const signOut = findButtonByText(fixture.nativeElement, 'Sign out');
    expect(signOut).toBeTruthy();
    signOut!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const modal = fixture.nativeElement.querySelector('shared-confirmation-modal') as HTMLElement;
    expect(modal).toBeTruthy();
    const confirm = findButtonByText(modal, 'Sign out');
    expect(confirm).toBeTruthy();
    confirm!.click();
    // The confirmation modal emits `confirm` only once its exit animation has played.
    await new Promise<void>(resolve => setTimeout(resolve, MODAL_EXIT_MS));
    await fixture.whenStable();

    expect(connectSpy.signOut).toHaveBeenCalledTimes(1);
  });

  it('keeps showing the account while connectivity is offline', async () => {
    configure(session({
      status: 'signedIn',
      connectivity: 'offline',
      offlineSince: '2026-08-18T00:00:00Z',
      account: { subject: 'u1', displayName: 'Jane Doe', avatarAvailable: false, avatarVersion: null, creatorUsername: null, roles: [] },
    }));
    fixture = await create();

    expect(fixture.nativeElement.textContent).toContain('Jane Doe');
    expect(fixture.nativeElement.querySelector('shared-avatar')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('unreachable');
    expect(findButtonByText(fixture.nativeElement, 'Sign in')).toBeNull();
  });

  it('surfaces the suspension message and an action instead of a sign-out-only view', async () => {
    configure(session({ status: 'suspended', message: 'Your subscription has lapsed.' }));
    fixture = await create();

    expect(fixture.nativeElement.textContent).toContain('Your subscription has lapsed.');

    const enabledButtons = Array.from(fixture.nativeElement.querySelectorAll('button'))
      .filter((b): b is HTMLButtonElement => b instanceof HTMLButtonElement && !b.disabled);
    expect(enabledButtons.length).toBeGreaterThan(0);

    expect(fixture.nativeElement.querySelector('.ls-spinner')).toBeNull();
  });

  it('offers a sign-in action when reauthentication is required', async () => {
    configure(session({ status: 'reauthenticationRequired', message: 'Please sign in again.' }));
    fixture = await create();

    const signIn = findButtonByText(fixture.nativeElement, 'Sign in');
    expect(signIn).toBeTruthy();
    expect(findButtonByText(fixture.nativeElement, 'Sign out')).toBeNull();

    signIn!.click();
    await fixture.whenStable();

    expect(connectSpy.signIn).toHaveBeenCalledTimes(1);
  });

  // Everything the user needs to finish on another device, and the states an attempt can end in
  // without a session (issue #673).
  it('shows the code, the page to reach by hand and the remaining validity while signing in', async () => {
    configure(session({ status: 'signingIn' }));
    fixture = await create();
    promptSignal.set({
      verificationUriComplete: 'https://accounts.macro-deck.app/device?user_code=3389-5291',
      verificationUri: 'https://accounts.macro-deck.app/device',
      userCode: '3389-5291',
      expiresAtUtc: new Date(Date.now() + (9 * 60_000) + 5_000).toISOString(),
    });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('3389-5291');
    expect(text).toContain('https://accounts.macro-deck.app/device');
    expect(text).toContain('9:0');
  });

  it('offers a restart with the reason when an attempt expired without being confirmed', async () => {
    configure(session({ status: 'signedOut', signInFailure: 'expired' }));
    fixture = await create();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('expired');
    expect(findButtonByText(fixture.nativeElement, 'Try again')).toBeTruthy();
  });

  it('says a declined authorization was declined rather than retrying silently', async () => {
    configure(session({ status: 'signedOut', signInFailure: 'denied' }));
    fixture = await create();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('declined');
  });
});
