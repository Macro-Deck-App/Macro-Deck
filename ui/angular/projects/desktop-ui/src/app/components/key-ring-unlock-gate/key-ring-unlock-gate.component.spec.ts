import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GetKeyRingStatusResponse, UnlockKeyRingResponse } from '@macro-deck/runtime';
import { KeyRingService } from '@shared';
import { KeyRingUnlockGateComponent } from './key-ring-unlock-gate.component';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

interface KeyRingServiceStub {
  status: WritableSignal<GetKeyRingStatusResponse | null>;
  locked: WritableSignal<boolean>;
  probe: jasmine.Spy;
  unlock: jasmine.Spy;
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
});
