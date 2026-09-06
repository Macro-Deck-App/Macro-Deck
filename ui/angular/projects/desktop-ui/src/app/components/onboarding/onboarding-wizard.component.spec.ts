import { provideZonelessChangeDetection, signal, WritableSignal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { GetBackupRecoveryKeyStateResponse, GetConnectionInfoResponse } from '@macro-deck/runtime';
import { ApiService, AuthService, AuthState, ConnectionState, ToastService } from '@shared';
import { OnboardingWizardComponent } from './onboarding-wizard.component';
import { BackupService } from '../../services/backup.service';
import { ExternalLinkService } from '../../services/external-link.service';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

const LAN_ENDPOINT: GetConnectionInfoResponse = {
  instanceName: 'Studio',
  version: '3.0.0',
  publicListenerUnavailable: false,
  endpoints: [{ address: '192.168.1.5', port: 8191, ssl: false }],
};

function recoveryKeyState(overrides: Partial<GetBackupRecoveryKeyStateResponse> = {}): GetBackupRecoveryKeyStateResponse {
  return {
    availability: 'Available',
    keyId: 'k1',
    createdAt: '2026-08-01T00:00:00Z',
    exportedAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

// Tests that do not care about the recovery-key step seed it as already satisfied: the key was
// exported before onboarding ever ran.
const ALREADY_EXPORTED = recoveryKeyState();

describe('OnboardingWizardComponent', () => {
  let fixture: ComponentFixture<OnboardingWizardComponent>;
  let authState: WritableSignal<AuthState>;
  let connectionState: WritableSignal<ConnectionState>;
  let api: jasmine.SpyObj<ApiService>;
  let externalLinks: jasmine.SpyObj<ExternalLinkService>;
  let backupService: jasmine.SpyObj<BackupService>;
  let recoveryKeySignal: WritableSignal<GetBackupRecoveryKeyStateResponse | null>;
  let toast: jasmine.SpyObj<ToastService>;

  // The wizard reaches the screen through an effect that awaits the host, so a single stability pass
  // is not enough: each round lets one more await settle before the next render.
  async function settle(): Promise<HTMLElement | null> {
    for (let round = 0; round < 4; round++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await new Promise(resolve => setTimeout(resolve));
    }

    fixture.detectChanges();
    return fixture.nativeElement.querySelector('shared-modal');
  }

  function buttonNamed(root: Element, label: string): HTMLButtonElement | undefined {
    return Array.from(root.querySelectorAll('shared-button button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent?.trim() === label);
  }

  async function click(button: HTMLButtonElement | undefined): Promise<void> {
    button?.click();
    await settle();
  }

  // A shared-modal runs a real 150ms close animation before it fires its output, so dismissing one
  // has to be waited out in wall-clock time. settle()'s zero-delay ticks cannot do that.
  async function dismissClick(button: HTMLButtonElement | undefined): Promise<void> {
    button?.click();
    await new Promise(resolve => setTimeout(resolve, 200));
    await settle();
  }

  function closeButtonOf(root: Element): HTMLButtonElement {
    return root.querySelector('button[aria-label="Close"]') as HTMLButtonElement;
  }

  async function create(
    pending: boolean,
    connection = LAN_ENDPOINT,
    initialRecoveryKey: GetBackupRecoveryKeyStateResponse | null = ALREADY_EXPORTED,
  ): Promise<void> {
    authState = signal<AuthState>('unknown');
    connectionState = signal<ConnectionState>('disconnected');
    api = jasmine.createSpyObj<ApiService>(
      'ApiService', ['getOnboardingState', 'completeOnboarding', 'getConnectionInfo']);
    api.getOnboardingState.and.resolveTo({ pending });
    api.completeOnboarding.and.resolveTo({ pending: false });
    api.getConnectionInfo.and.resolveTo(connection);
    externalLinks = jasmine.createSpyObj<ExternalLinkService>('ExternalLinkService', ['open']);

    recoveryKeySignal = signal(initialRecoveryKey);
    backupService = jasmine.createSpyObj<BackupService>(
      'BackupService', ['loadRecoveryKeyState', 'createRecoveryKey', 'revealRecoveryKey', 'acknowledgeRecoveryKey']);
    Object.defineProperty(backupService, 'recoveryKey', { value: recoveryKeySignal });
    backupService.loadRecoveryKeyState.and.resolveTo(true);

    toast = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    await TestBed.configureTestingModule({
      imports: [OnboardingWizardComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: AuthService, useValue: { state: authState.asReadonly() } },
        {
          provide: ApiService,
          useValue: Object.assign(api, {
            connectionStateSignal: connectionState.asReadonly(),
            onNotification: () => EMPTY,
          }),
        },
        { provide: ExternalLinkService, useValue: externalLinks },
        { provide: BackupService, useValue: backupService },
        { provide: ToastService, useValue: toast },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(OnboardingWizardComponent);
  }

  async function openConnectStep(): Promise<HTMLElement> {
    const wizard = await settle();
    await click(buttonNamed(wizard!, 'Next'));
    return (await settle())!;
  }

  async function openRecoveryKeyStep(): Promise<HTMLElement> {
    const connect = await openConnectStep();
    await click(buttonNamed(connect, 'Next'));
    return (await settle())!;
  }

  it('waits for the app to be both connected and authenticated', async () => {
    await create(true);

    expect(await settle()).toBeNull();

    authState.set('authenticated');
    expect(await settle()).toBeNull();

    connectionState.set('connected');
    expect(await settle()).toBeTruthy();
  });

  it('never appears for a user the host owes no wizard', async () => {
    await create(false);
    authState.set('authenticated');
    connectionState.set('connected');

    expect(await settle()).toBeNull();
    expect(api.completeOnboarding).not.toHaveBeenCalled();
  });

  async function walkToTheEnd(): Promise<void> {
    let wizard = await settle();
    for (let step = 0; step < 3; step++) {
      await click(buttonNamed(wizard!, 'Next'));
      wizard = await settle();
    }
    await click(buttonNamed(wizard!, 'Get started'));
  }

  it('does not come back after it was finished, not even on a reconnect', async () => {
    await create(true);
    authState.set('authenticated');
    connectionState.set('connected');
    await walkToTheEnd();

    connectionState.set('disconnected');
    await settle();
    connectionState.set('connected');

    expect(await settle()).toBeNull();
    expect(api.getOnboardingState).toHaveBeenCalledTimes(1);
    expect(api.completeOnboarding).toHaveBeenCalledTimes(1);
  });

  it('offers no way out of the wizard other than walking it to the end', async () => {
    await create(true);
    authState.set('authenticated');
    connectionState.set('connected');
    let wizard = await settle();

    for (let step = 0; step < 4; step++) {
      expect(buttonNamed(wizard!, 'Skip')).toBeUndefined();
      expect(closeButtonOf(wizard!)).toBeNull();
      if (step < 3) {
        await click(buttonNamed(wizard!, 'Next'));
        wizard = await settle();
      }
    }

    expect(api.completeOnboarding).not.toHaveBeenCalled();
    await click(buttonNamed(wizard!, 'Get started'));
    expect(api.completeOnboarding).toHaveBeenCalledTimes(1);
  });

  it('records finishing the last step with the host', async () => {
    await create(true);
    authState.set('authenticated');
    connectionState.set('connected');
    let wizard = await settle();
    await click(buttonNamed(wizard!, 'Next'));
    wizard = (await settle())!;
    await click(buttonNamed(wizard, 'Next'));
    wizard = (await settle())!;
    expect(buttonNamed(wizard, 'Next')?.disabled).toBeFalse();
    await click(buttonNamed(wizard, 'Next'));
    wizard = (await settle())!;

    await click(buttonNamed(wizard, 'Get started'));

    expect(api.completeOnboarding).toHaveBeenCalledTimes(1);
    expect(await settle()).toBeNull();
  });

  it('walks welcome, connect, recovery key and useful links, and back again', async () => {
    await create(true);
    authState.set('authenticated');
    connectionState.set('connected');
    let wizard = await settle();

    expect(wizard!.textContent).toContain('Welcome to Macro Deck 3');

    await click(buttonNamed(wizard!, 'Next'));
    wizard = (await settle())!;
    expect(wizard.textContent).toContain('Connect to Macro Deck');

    await click(buttonNamed(wizard, 'Next'));
    wizard = (await settle())!;
    expect(wizard.textContent).toContain('Your backup recovery key');
    expect(buttonNamed(wizard, 'Next')?.disabled).toBeFalse();

    await click(buttonNamed(wizard, 'Next'));
    wizard = (await settle())!;
    expect(wizard.textContent).toContain('Useful links');
    expect(buttonNamed(wizard, 'Next')).toBeUndefined();

    await click(buttonNamed(wizard, 'Back'));
    wizard = (await settle())!;
    expect(wizard.textContent).toContain('Your backup recovery key');
  });

  it('offers every address the host reported, not one it picked itself', async () => {
    await create(true, {
      ...LAN_ENDPOINT,
      endpoints: [
        { address: '192.168.1.5', port: 8191, ssl: false },
        { address: '10.8.0.2', port: 8191, ssl: false },
      ],
    });
    authState.set('authenticated');
    connectionState.set('connected');
    const connect = await openConnectStep();

    const shown = Array.from(connect.querySelectorAll('.ob-address-url')).map(node => node.textContent);

    expect(shown).toEqual(['http://192.168.1.5:8191', 'http://10.8.0.2:8191']);
  });

  it('lists the plain addresses before the secure ones, keeping the host order within each', async () => {
    await create(true, {
      ...LAN_ENDPOINT,
      endpoints: [
        { address: '192.168.1.5', port: 8191, ssl: false },
        { address: '192.168.1.5', port: 7194, ssl: true },
        { address: '10.8.0.2', port: 8191, ssl: false },
        { address: '10.8.0.2', port: 7194, ssl: true },
      ],
    });
    authState.set('authenticated');
    connectionState.set('connected');
    const connect = await openConnectStep();

    const shown = Array.from(connect.querySelectorAll('.ob-address-url')).map(node => node.textContent);

    expect(shown).toEqual([
      'http://192.168.1.5:8191',
      'http://10.8.0.2:8191',
      'https://192.168.1.5:7194',
      'https://10.8.0.2:7194',
    ]);
  });

  it('opens the configuration UI and the web client at the address that was picked', async () => {
    await create(true, {
      ...LAN_ENDPOINT,
      endpoints: [
        { address: '192.168.1.5', port: 8191, ssl: false },
        { address: '10.8.0.2', port: 8191, ssl: false },
      ],
    });
    authState.set('authenticated');
    connectionState.set('connected');
    const connect = await openConnectStep();

    const second = connect.querySelectorAll('.ob-address')[1];
    await click(buttonNamed(second, 'Configuration UI'));
    await click(buttonNamed(connect.querySelectorAll('.ob-address')[1], 'Web Client'));

    expect(externalLinks.open.calls.allArgs()).toEqual([
      ['http://10.8.0.2:8191/admin'],
      ['http://10.8.0.2:8191'],
    ]);
  });

  it('uses the scheme the endpoint reports rather than assuming plain HTTP', async () => {
    await create(true, {
      ...LAN_ENDPOINT,
      endpoints: [{ address: 'macro-deck.local', port: 8192, ssl: true }],
    });
    authState.set('authenticated');
    connectionState.set('connected');
    const connect = await openConnectStep();

    await click(buttonNamed(connect, 'Configuration UI'));

    expect(externalLinks.open).toHaveBeenCalledWith('https://macro-deck.local:8192/admin');
  });

  it('presents the native app as unavailable and offers nothing to click there', async () => {
    await create(true);
    authState.set('authenticated');
    connectionState.set('connected');
    const connect = await openConnectStep();

    const nativeApp = connect.querySelector('.ob-option-unavailable')!;
    expect(nativeApp.textContent).toContain('Coming soon');
    expect(nativeApp.textContent).toContain('web client');
    expect(nativeApp.querySelectorAll('button, a, [role="button"]').length).toBe(0);
  });

  it('stays usable when the host cannot report how it is reachable', async () => {
    await create(true, { ...LAN_ENDPOINT, publicListenerUnavailable: true, endpoints: [] });
    authState.set('authenticated');
    connectionState.set('connected');
    const recoveryKey = await openRecoveryKeyStep();

    await click(buttonNamed(recoveryKey, 'Next'));
    const links = (await settle())!;
    await click(buttonNamed(links, 'Get started'));

    expect(externalLinks.open).not.toHaveBeenCalled();
    expect(api.completeOnboarding).toHaveBeenCalledTimes(1);
  });

  it('opens each project link in the browser', async () => {
    await create(true);
    authState.set('authenticated');
    connectionState.set('connected');
    let wizard = await settle();
    await click(buttonNamed(wizard!, 'Next'));
    wizard = (await settle())!;
    await click(buttonNamed(wizard, 'Next'));
    wizard = (await settle())!;
    await click(buttonNamed(wizard, 'Next'));
    wizard = (await settle())!;

    for (const button of Array.from(
      wizard.querySelectorAll('shared-button button') as NodeListOf<HTMLButtonElement>)
      .filter(button => button.textContent?.trim() === 'Open')) {
      await click(button);
    }

    expect(externalLinks.open.calls.allArgs()).toEqual([
      ['https://github.com/Macro-Deck-App/Macro-Deck'],
      ['https://github.com/sponsors/Macro-Deck-App'],
      ['https://ko-fi.com/manuelmayer'],
      ['https://discord.macro-deck.app'],
    ]);
    expect(wizard.querySelectorAll('a[href]').length).toBe(0);
  });

  describe('the backup recovery key step', () => {
    it('says what the key is, the risk of losing it, and both reasons it may be needed again', async () => {
      await create(true, LAN_ENDPOINT, recoveryKeyState({ exportedAt: undefined }));
      authState.set('authenticated');
      connectionState.set('connected');
      const step = await openRecoveryKeyStep();
      const text = step.textContent ?? '';

      expect(text).toContain('This recovery key is what decrypts one again');
      expect(text).toContain('nobody can unlock them for you, not even Macro Deck');
      expect(text).toContain('To restore a backup on another computer, you need this key');
      expect(text).toContain('reinstalling the operating system');
    });

    it('shows a freshly created key once and does not re-show it or re-create it later', async () => {
      const created = recoveryKeyState({ exportedAt: '2026-09-01T00:00:00Z' });
      await create(true, LAN_ENDPOINT, recoveryKeyState({ availability: 'None', exportedAt: undefined, createdAt: undefined }));
      backupService.createRecoveryKey.and.callFake(async () => {
        recoveryKeySignal.set(created);
        return { status: 'success', key: 'MDBK-NEW-KEY-0000', state: created };
      });
      backupService.acknowledgeRecoveryKey.and.resolveTo({ ok: true });
      authState.set('authenticated');
      connectionState.set('connected');
      let step = await openRecoveryKeyStep();

      await click(buttonNamed(step, 'Create recovery key'));
      step = (await settle())!;

      expect(backupService.createRecoveryKey).toHaveBeenCalledTimes(1);
      expect(step.textContent).toContain('MDBK-NEW-KEY-0000');

      const checkbox = step.querySelector('input[type="checkbox"]') as HTMLInputElement;
      checkbox.click();
      step = (await settle())!;
      await dismissClick(buttonNamed(step, 'Done'));
      step = (await settle())!;

      expect(backupService.acknowledgeRecoveryKey).toHaveBeenCalledTimes(1);

      await click(buttonNamed(step, 'Back'));
      step = (await settle())!;
      await click(buttonNamed(step, 'Next'));
      step = (await settle())!;

      expect(step.textContent).not.toContain('MDBK-NEW-KEY-0000');
      expect(backupService.createRecoveryKey).toHaveBeenCalledTimes(1);
    });

    it('records acknowledgement and unlocks the step only after the checkbox is confirmed', async () => {
      const created = recoveryKeyState({ exportedAt: '2026-09-01T00:00:00Z' });
      await create(true, LAN_ENDPOINT, recoveryKeyState({ availability: 'None', exportedAt: undefined, createdAt: undefined }));
      backupService.createRecoveryKey.and.callFake(async () => {
        recoveryKeySignal.set(created);
        return { status: 'success', key: 'MDBK-NEW-KEY-0000', state: created };
      });
      backupService.acknowledgeRecoveryKey.and.resolveTo({ ok: true });
      authState.set('authenticated');
      connectionState.set('connected');
      let step = await openRecoveryKeyStep();

      await click(buttonNamed(step, 'Create recovery key'));
      step = (await settle())!;
      expect(buttonNamed(step, 'Next')?.disabled).toBeTrue();

      const checkbox = step.querySelector('input[type="checkbox"]') as HTMLInputElement;
      checkbox.click();
      step = (await settle())!;
      await dismissClick(buttonNamed(step, 'Done'));
      step = (await settle())!;

      expect(backupService.acknowledgeRecoveryKey).toHaveBeenCalledTimes(1);
      expect(api.completeOnboarding).not.toHaveBeenCalled();
      expect(buttonNamed(step, 'Next')?.disabled).toBeFalse();

      await click(buttonNamed(step, 'Next'));
      step = (await settle())!;
      await click(buttonNamed(step, 'Get started'));

      expect(api.completeOnboarding).toHaveBeenCalledTimes(1);
    });

    it('does not unlock on a successful create alone, even when the response already carries an exportedAt', async () => {
      const created = recoveryKeyState({ exportedAt: '2026-09-01T00:00:00Z' });
      await create(true, LAN_ENDPOINT, recoveryKeyState({ availability: 'None', exportedAt: undefined, createdAt: undefined }));
      backupService.createRecoveryKey.and.callFake(async () => {
        recoveryKeySignal.set(created);
        return { status: 'success', key: 'MDBK-NEW-KEY-0000', state: created };
      });
      authState.set('authenticated');
      connectionState.set('connected');
      let step = await openRecoveryKeyStep();

      await click(buttonNamed(step, 'Create recovery key'));
      step = (await settle())!;

      expect(buttonNamed(step, 'Next')?.disabled).toBeTrue();
      expect(backupService.acknowledgeRecoveryKey).not.toHaveBeenCalled();
    });

    it('leaves the step locked but reachable when the key modal is dismissed without confirming', async () => {
      const created = recoveryKeyState({ exportedAt: '2026-09-01T00:00:00Z' });
      await create(true, LAN_ENDPOINT, recoveryKeyState({ availability: 'None', exportedAt: undefined, createdAt: undefined }));
      backupService.createRecoveryKey.and.callFake(async () => {
        recoveryKeySignal.set(created);
        return { status: 'success', key: 'MDBK-NEW-KEY-0000', state: created };
      });
      authState.set('authenticated');
      connectionState.set('connected');
      let step = await openRecoveryKeyStep();

      await click(buttonNamed(step, 'Create recovery key'));
      step = (await settle())!;

      await dismissClick(closeButtonOf(step.querySelector('shared-recovery-key-modal')!));
      step = (await settle())!;

      expect(backupService.acknowledgeRecoveryKey).not.toHaveBeenCalled();
      expect(buttonNamed(step, 'Next')?.disabled).toBeTrue();
      expect(step.querySelector('shared-recovery-key-modal')).toBeNull();
      expect(buttonNamed(step, 'Show recovery key')).toBeTruthy();
    });

    it('offers no close button to get past the unsaved key with', async () => {
      await create(true, LAN_ENDPOINT, recoveryKeyState({ availability: 'None', exportedAt: undefined, createdAt: undefined }));
      authState.set('authenticated');
      connectionState.set('connected');
      const step = await openRecoveryKeyStep();

      expect(closeButtonOf(step)).toBeNull();
      expect(buttonNamed(step, 'Skip')).toBeUndefined();
      expect(api.completeOnboarding).not.toHaveBeenCalled();
    });

    it('is not a dead end when the key cannot be created', async () => {
      await create(true, LAN_ENDPOINT, recoveryKeyState({ availability: 'None', exportedAt: undefined, createdAt: undefined }));
      backupService.createRecoveryKey.and.resolveTo({ status: 'error', message: 'nope' });
      authState.set('authenticated');
      connectionState.set('connected');
      let step = await openRecoveryKeyStep();

      await click(buttonNamed(step, 'Create recovery key'));
      step = (await settle())!;

      expect(step.textContent).toContain('could not be created');
      expect(buttonNamed(step, 'Next')?.disabled).toBeFalse();

      await click(buttonNamed(step, 'Next'));
      step = (await settle())!;
      await click(buttonNamed(step, 'Get started'));

      expect(api.completeOnboarding).toHaveBeenCalledTimes(1);
    });

    it('is not a dead end when the recovery key is missing', async () => {
      await create(true, LAN_ENDPOINT, recoveryKeyState({ availability: 'Missing', exportedAt: undefined }));
      authState.set('authenticated');
      connectionState.set('connected');
      let step = await openRecoveryKeyStep();

      expect(step.textContent).toContain('could not be read');
      expect(buttonNamed(step, 'Next')?.disabled).toBeFalse();

      await click(buttonNamed(step, 'Next'));
      step = (await settle())!;
      await click(buttonNamed(step, 'Get started'));

      expect(api.completeOnboarding).toHaveBeenCalledTimes(1);
      expect(backupService.acknowledgeRecoveryKey).not.toHaveBeenCalled();
    });

    it('is not a dead end when revealing the key fails because this is not the machine running Macro Deck', async () => {
      await create(true, LAN_ENDPOINT, recoveryKeyState({ exportedAt: undefined }));
      backupService.revealRecoveryKey.and.resolveTo({ status: 'error', message: 'nope' });
      authState.set('authenticated');
      connectionState.set('connected');
      let step = await openRecoveryKeyStep();

      await click(buttonNamed(step, 'Show recovery key'));
      step = (await settle())!;
      await dismissClick(buttonNamed(step, 'Show key'));
      step = (await settle())!;

      expect(step.textContent).toContain('can only be shown on the computer running Macro Deck');
      expect(step.querySelector('shared-recovery-key-modal')).toBeNull();
      expect(buttonNamed(step, 'Show recovery key')).toBeTruthy();
      expect(buttonNamed(step, 'Next')?.disabled).toBeFalse();
    });

    it('is not a dead end when the recovery key state never loads', async () => {
      await create(true, LAN_ENDPOINT, null);
      backupService.loadRecoveryKeyState.and.resolveTo(false);
      authState.set('authenticated');
      connectionState.set('connected');
      let step = await openRecoveryKeyStep();

      expect(step.textContent).toContain('The recovery key state could not be loaded');
      expect(buttonNamed(step, 'Retry')).toBeTruthy();
      expect(buttonNamed(step, 'Next')?.disabled).toBeFalse();

      await click(buttonNamed(step, 'Next'));
      step = (await settle())!;
      await click(buttonNamed(step, 'Get started'));

      expect(api.completeOnboarding).toHaveBeenCalledTimes(1);
    });

    it('asks for nothing when the key was already exported before onboarding ran', async () => {
      await create(true, LAN_ENDPOINT, ALREADY_EXPORTED);
      authState.set('authenticated');
      connectionState.set('connected');
      const step = await openRecoveryKeyStep();

      expect(buttonNamed(step, 'Next')?.disabled).toBeFalse();
      expect(backupService.loadRecoveryKeyState).not.toHaveBeenCalled();
      expect(backupService.createRecoveryKey).not.toHaveBeenCalled();
      expect(backupService.revealRecoveryKey).not.toHaveBeenCalled();
      expect(backupService.acknowledgeRecoveryKey).not.toHaveBeenCalled();
    });
  });
});
