import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  GetKeyRingStatusResponse,
  KeyRingKeystoreRetryOutcome,
  RetryKeyRingKeystoreResponse,
  UnlockKeyRingResponse,
} from '@macro-deck/runtime';
import { KeyRingService } from '@shared';
import { KeyRingUnlockGateComponent } from './key-ring-unlock-gate.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

interface KeyRingServiceStub {
  status: WritableSignal<GetKeyRingStatusResponse | null>;
  locked: WritableSignal<boolean>;
  probe: jasmine.Spy;
  unlock: jasmine.Spy;
  retryKeystore: jasmine.Spy;
}

function retried(outcome: KeyRingKeystoreRetryOutcome, restartRequested = false): RetryKeyRingKeystoreResponse {
  return { outcome, restartRequested, restartSupported: true, restartUnsupportedReason: null };
}

function keystoreFailure(): UnlockKeyRingResponse {
  return {
    success: false,
    error: { code: 'KeystoreUnavailable', message: 'nope' },
    restartRequested: false,
    restartSupported: true,
    restartUnsupportedReason: null,
  };
}

function status(overrides: Partial<GetKeyRingStatusResponse> = {}): GetKeyRingStatusResponse {
  return {
    locked: true,
    lockReason: 'KeystoreEntryMissing',
    restartSupported: true,
    restartUnsupportedReason: null,
    ...overrides,
  };
}

function keyRingStub(initial: GetKeyRingStatusResponse | null): KeyRingServiceStub {
  return {
    status: signal(initial),
    locked: signal(initial?.locked ?? false),
    probe: jasmine.createSpy('probe').and.resolveTo(undefined),
    unlock: jasmine.createSpy('unlock'),
    retryKeystore: jasmine.createSpy('retryKeystore'),
  };
}

describe('KeyRingUnlockGateComponent', () => {
  let stub: KeyRingServiceStub;

  function create(initial: GetKeyRingStatusResponse | null): ComponentFixture<KeyRingUnlockGateComponent> {
    stub = keyRingStub(initial);
    TestBed.configureTestingModule({
      imports: [KeyRingUnlockGateComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: KeyRingService, useValue: stub },
      ],
    });
    const fixture = TestBed.createComponent(KeyRingUnlockGateComponent);
    fixture.detectChanges();
    return fixture;
  }

  function recoveryKeyInput(fixture: ComponentFixture<KeyRingUnlockGateComponent>): HTMLInputElement | null {
    return fixture.nativeElement.querySelector('shared-recovery-key-prompt-modal shared-input input');
  }

  function submitButton(fixture: ComponentFixture<KeyRingUnlockGateComponent>): HTMLButtonElement | undefined {
    return Array.from(fixture.nativeElement.querySelectorAll('shared-recovery-key-prompt-modal shared-button button'))
      .find(el => (el as HTMLElement).textContent?.trim() === 'Unlock') as HTMLButtonElement | undefined;
  }

  it('hosts the recovery-key input for a recoverable lock reason, explaining it', () => {
    const fixture = create(status({ lockReason: 'KeystoreEntryStale' }));

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('shared-recovery-key-prompt-modal')).toBeTruthy();
    expect(recoveryKeyInput(fixture)).toBeTruthy();
    expect(submitButton(fixture)).toBeTruthy();
    expect(element.textContent).toContain('This computer\'s secret storage holds a different key');
  });

  it('shows the dead-end message and no input for EscrowMissing', () => {
    const fixture = create(status({ lockReason: 'EscrowMissing' }));

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('shared-recovery-key-prompt-modal')).toBeNull();
    expect(element.querySelector('input')).toBeNull();
    expect(element.querySelector('shared-button')).toBeNull();
    expect(element.textContent).toContain('The recovery file that would restore the key is missing');
  });

  it('flips to a retry state - staying visible and usable - when the host reports an invalid recovery key', async () => {
    const fixture = create(status());
    stub.unlock.and.resolveTo({
      success: false,
      error: { code: 'RecoveryKeyInvalid', message: 'nope' },
      restartRequested: false,
      restartSupported: true,
      restartUnsupportedReason: null,
    } satisfies UnlockKeyRingResponse);

    const input = recoveryKeyInput(fixture)!;
    input.value = 'WRONG-KEY';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    submitButton(fixture)!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(stub.unlock).toHaveBeenCalledWith('WRONG-KEY');

    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('That recovery key does not unlock this installation');
    // Submitting must not play the modal's dismiss animation here: there is nothing to dismiss into,
    // and ModalComponent.closing never resets, so the prompt must stay fully visible for the retry.
    expect(recoveryKeyInput(fixture)).toBeTruthy();
    const overlay = element.querySelector('shared-recovery-key-prompt-modal .modal-overlay');
    expect(overlay?.classList.contains('closing')).toBeFalse();
  });

  it('shows the restart-required message once the host accepts the recovery key', async () => {
    const fixture = create(status());
    stub.unlock.and.resolveTo({
      success: true,
      error: null,
      restartRequested: true,
      restartSupported: true,
      restartUnsupportedReason: null,
    } satisfies UnlockKeyRingResponse);

    const input = recoveryKeyInput(fixture)!;
    input.value = 'CORRECT-KEY';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    submitButton(fixture)!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('shared-recovery-key-prompt-modal')).toBeNull();
    expect(element.textContent).toContain('Restarting Macro Deck to finish unlocking');
  });

  it('surfaces a KeystoreUnavailable unlock error in a banner instead of the retry state', async () => {
    const fixture = create(status());
    stub.unlock.and.resolveTo({
      success: false,
      error: { code: 'KeystoreUnavailable', message: 'nope' },
      restartRequested: false,
      restartSupported: true,
      restartUnsupportedReason: null,
    } satisfies UnlockKeyRingResponse);

    const input = recoveryKeyInput(fixture)!;
    input.value = 'SOME-KEY';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    submitButton(fixture)!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('shared-error-banner')).toBeTruthy();
    expect(element.textContent).toContain('The key could not be saved to this computer\'s secret storage');
  });

  describe('when the secret storage could not be reached', () => {
    const unreachable = () => status({ lockReason: 'KeystoreUnavailable' });

    function button(fixture: ComponentFixture<KeyRingUnlockGateComponent>, label: string): HTMLButtonElement {
      return Array.from(fixture.nativeElement.querySelectorAll('shared-button button') as NodeListOf<HTMLButtonElement>)
        .find(el => el.textContent?.trim() === label)!;
    }

    async function settle(fixture: ComponentFixture<KeyRingUnlockGateComponent>): Promise<void> {
      fixture.detectChanges();
      await new Promise(resolve => setTimeout(resolve));
      await fixture.whenStable();
      fixture.detectChanges();
    }

    async function settleUntil(
      fixture: ComponentFixture<KeyRingUnlockGateComponent>,
      done: () => boolean,
    ): Promise<void> {
      for (let attempt = 0; attempt < 100 && !done(); attempt++) {
        await new Promise(resolve => setTimeout(resolve, 20));
        fixture.detectChanges();
      }
    }

    async function tryAgain(fixture: ComponentFixture<KeyRingUnlockGateComponent>): Promise<void> {
      button(fixture, 'Try again').click();
      await settle(fixture);
    }

    async function typeAndUnlock(fixture: ComponentFixture<KeyRingUnlockGateComponent>): Promise<void> {
      const input = recoveryKeyInput(fixture)!;
      input.value = 'SOME-KEY';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      submitButton(fixture)!.click();
      await settle(fixture);
    }

    it('explains that the secret storage must be unlocked instead of asking for the recovery key', () => {
      const fixture = create(unreachable());

      const element: HTMLElement = fixture.nativeElement;
      expect(recoveryKeyInput(fixture)).toBeNull();
      expect(element.textContent).toContain('Macro Deck cannot reach your secret storage');
      expect(element.textContent).toContain('Unlock it, then try again');
      expect(button(fixture, 'Try again')).toBeTruthy();
      expect(button(fixture, 'Use recovery key instead')).toBeTruthy();
    });

    it('finishes unlocking without a recovery key once the secret storage answers', async () => {
      const fixture = create(unreachable());
      stub.retryKeystore.and.resolveTo(retried('Recovered', true));

      await tryAgain(fixture);

      const element: HTMLElement = fixture.nativeElement;
      expect(element.textContent).toContain('Restarting Macro Deck to finish unlocking');
      expect(element.textContent).not.toContain('Enter your recovery key');
      expect(stub.unlock).not.toHaveBeenCalled();
    });

    it('says the secret storage is still unreachable and points at the recovery key', async () => {
      const fixture = create(unreachable());
      stub.retryKeystore.and.resolveTo(retried('Unavailable'));

      await tryAgain(fixture);

      const element: HTMLElement = fixture.nativeElement;
      expect(element.textContent).toContain('The secret storage still cannot be reached');
      expect(element.textContent).toContain('use your recovery key instead');
      expect(button(fixture, 'Try again').disabled).toBeFalse();
    });

    it('keeps asking while the secret storage waits for its password, and says so', async () => {
      const fixture = create(unreachable());
      let answer!: (response: RetryKeyRingKeystoreResponse) => void;
      stub.retryKeystore.and.returnValues(
        Promise.resolve(retried('Pending')),
        new Promise<RetryKeyRingKeystoreResponse>(resolve => (answer = resolve)),
      );

      await tryAgain(fixture);

      const element: HTMLElement = fixture.nativeElement;
      expect(element.textContent).toContain('If it asks for its password, enter it');
      expect(stub.retryKeystore).toHaveBeenCalledTimes(2);

      answer(retried('Recovered', true));
      await settle(fixture);

      expect(element.textContent).toContain('Restarting Macro Deck to finish unlocking');
    });

    it('asks for the recovery key with the real reason once the secret storage answers without the key', async () => {
      const fixture = create(unreachable());
      stub.retryKeystore.and.resolveTo(retried('EntryStale'));

      await tryAgain(fixture);

      const element: HTMLElement = fixture.nativeElement;
      expect(recoveryKeyInput(fixture)).toBeTruthy();
      expect(element.textContent).toContain('This computer\'s secret storage holds a different key');
      expect(element.textContent).not.toContain('Cancel');
    });

    it('reports a retry the host could not answer instead of failing silently', async () => {
      const fixture = create(unreachable());
      stub.retryKeystore.and.rejectWith(new Error('offline'));

      await tryAgain(fixture);

      const element: HTMLElement = fixture.nativeElement;
      expect(element.querySelector('shared-error-banner')).toBeTruthy();
      expect(button(fixture, 'Try again').disabled).toBeFalse();
    });

    it('checks the host again when the retry says the lock is no longer about the secret storage', async () => {
      const fixture = create(unreachable());
      stub.retryKeystore.and.resolveTo(retried('NotApplicable'));

      await tryAgain(fixture);

      expect(stub.probe).toHaveBeenCalled();
    });

    it('offers the recovery key on request and leads back to the guidance', async () => {
      const fixture = create(unreachable());

      button(fixture, 'Use recovery key instead').click();
      await settle(fixture);
      expect(recoveryKeyInput(fixture)).toBeTruthy();

      button(fixture, 'Cancel').click();
      await settleUntil(fixture, () => recoveryKeyInput(fixture) === null);

      expect(recoveryKeyInput(fixture)).toBeNull();
      expect(button(fixture, 'Try again')).toBeTruthy();
    });

    it('ignores a late answer after the user switched to the recovery key', async () => {
      const fixture = create(unreachable());
      let answer!: (response: RetryKeyRingKeystoreResponse) => void;
      stub.retryKeystore.and.returnValue(new Promise<RetryKeyRingKeystoreResponse>(resolve => (answer = resolve)));
      button(fixture, 'Try again').click();
      fixture.detectChanges();

      button(fixture, 'Use recovery key instead').click();
      await settle(fixture);
      answer(retried('Pending'));
      await settle(fixture);

      expect(recoveryKeyInput(fixture)).toBeTruthy();
      expect(stub.retryKeystore).toHaveBeenCalledTimes(1);
    });

    it('returns to the guidance when the recovery key cannot be saved to the unreachable secret storage', async () => {
      const fixture = create(unreachable());
      stub.unlock.and.resolveTo(keystoreFailure());
      button(fixture, 'Use recovery key instead').click();
      await settle(fixture);

      await typeAndUnlock(fixture);

      const element: HTMLElement = fixture.nativeElement;
      expect(recoveryKeyInput(fixture)).toBeNull();
      expect(element.textContent).toContain('The key could not be saved to this computer\'s secret storage');
      expect(element.textContent).toContain('Unlock it, then try again');
    });
  });
});
