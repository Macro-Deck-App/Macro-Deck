import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AppStrings, TransportError, UnlockKeyRingResponse } from '@macro-deck/runtime';
import { KeyRingService, LocalizationService, ModalComponent, TranslatePipe } from '@shared';
import { RecoveryKeyPromptModalComponent } from '../backup/recovery-key-prompt-modal/recovery-key-prompt-modal.component';

const REASON_KEYS: Record<string, string> = {
  KeystoreEntryMissing: AppStrings.KeyRing.Unlock.Reason.KeystoreEntryMissing,
  KeystoreEntryStale: AppStrings.KeyRing.Unlock.Reason.KeystoreEntryStale,
  KeystoreUnavailable: AppStrings.KeyRing.Unlock.Reason.KeystoreUnavailable,
};

const UNLOCK_ERROR_KEYS: Record<string, string> = {
  KeystoreUnavailable: AppStrings.Errors.KeyRing.KeystoreUnavailable,
  EscrowMissing: AppStrings.Errors.KeyRing.EscrowMissing,
  EscrowUnreadable: AppStrings.Errors.KeyRing.EscrowUnreadable,
  Locked: AppStrings.Errors.KeyRing.Locked,
};

@Component({
  selector: 'app-key-ring-unlock-gate',
  standalone: true,
  imports: [ModalComponent, RecoveryKeyPromptModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './key-ring-unlock-gate.component.html',
  styleUrls: ['./key-ring-unlock-gate.component.scss'],
})
export class KeyRingUnlockGateComponent {
  private readonly keyRing = inject(KeyRingService);
  private readonly localization = inject(LocalizationService);
  protected readonly appStrings = AppStrings;

  protected readonly status = this.keyRing.status;
  protected readonly submitting = signal(false);
  protected readonly invalid = signal(false);
  protected readonly bannerMessage = signal<string | null>(null);
  protected readonly result = signal<UnlockKeyRingResponse | null>(null);

  protected readonly deadEnd = computed(() => this.status()?.lockReason === 'EscrowMissing');

  private readonly reasonMessage = computed(() => {
    const reason = this.status()?.lockReason;
    const key = reason ? REASON_KEYS[reason] : undefined;
    return key ? this.localization.translateKey(key) : null;
  });

  protected readonly description = computed(() => {
    const base = this.localization.translateKey(AppStrings.KeyRing.Unlock.Description);
    const reason = this.reasonMessage();
    return reason ? `${base} ${reason}` : base;
  });

  async submit(recoveryKey: string): Promise<void> {
    if (this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.invalid.set(false);
    this.bannerMessage.set(null);
    try {
      const response = await this.keyRing.unlock(recoveryKey);
      if (response.success) {
        this.result.set(response);
        return;
      }
      const code = response.error?.code;
      if (code === 'RecoveryKeyInvalid') {
        this.invalid.set(true);
        return;
      }
      this.bannerMessage.set(this.unlockErrorMessage(code));
    } catch (error) {
      this.bannerMessage.set(error instanceof TransportError ? error.message : this.unlockErrorMessage(undefined));
    } finally {
      this.submitting.set(false);
    }
  }

  dismissBanner(): void {
    this.bannerMessage.set(null);
  }

  private unlockErrorMessage(code: string | undefined): string {
    const key = (code ? UNLOCK_ERROR_KEYS[code] : undefined) ?? AppStrings.Errors.KeyRing.KeystoreUnavailable;
    return this.localization.translateKey(key);
  }
}
