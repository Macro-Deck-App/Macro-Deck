import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { AppStrings, RetryKeyRingKeystoreResponse, TransportError, UnlockKeyRingResponse } from '@macro-deck/runtime';
import {
  ButtonComponent,
  ErrorBannerComponent,
  KeyRingService,
  LocalizationService,
  ModalComponent,
  TranslatePipe,
} from '@shared';
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

const RETRY_REASONS: Partial<Record<RetryKeyRingKeystoreResponse['outcome'], string>> = {
  EntryMissing: 'KeystoreEntryMissing',
  EntryStale: 'KeystoreEntryStale',
};

interface UnlockedResult {
  restartRequested: boolean;
  byRecoveryKey: boolean;
}

@Component({
  selector: 'app-key-ring-unlock-gate',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, ErrorBannerComponent, RecoveryKeyPromptModalComponent, TranslatePipe],
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
  protected readonly result = signal<UnlockedResult | null>(null);
  protected readonly retrying = signal(false);
  protected readonly waiting = signal(false);
  protected readonly keystoreMessage = signal<string | null>(null);

  private readonly recoveryKeyRequested = signal(false);
  private readonly retriedReason = signal<string | null>(null);
  private retryRun = 0;

  protected readonly deadEnd = computed(() => this.status()?.lockReason === 'EscrowMissing');

  private readonly keystoreUnreachable = computed(
    () => this.status()?.lockReason === 'KeystoreUnavailable' && this.retriedReason() === null,
  );

  protected readonly keystoreGuidance = computed(() => this.keystoreUnreachable() && !this.recoveryKeyRequested());

  protected readonly canReturnToKeystore = computed(() => this.keystoreUnreachable());

  private readonly reasonMessage = computed(() => {
    const reason = this.retriedReason() ?? this.status()?.lockReason;
    const key = reason ? REASON_KEYS[reason] : undefined;
    return key ? this.localization.translateKey(key) : null;
  });

  protected readonly description = computed(() => {
    const base = this.localization.translateKey(AppStrings.KeyRing.Unlock.Description);
    const reason = this.reasonMessage();
    return reason ? `${base} ${reason}` : base;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopRetrying());
  }

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
        this.result.set({ restartRequested: response.restartRequested, byRecoveryKey: true });
        return;
      }
      const code = response.error?.code;
      if (code === 'RecoveryKeyInvalid') {
        this.invalid.set(true);
        return;
      }
      if (code === 'KeystoreUnavailable' && this.keystoreUnreachable()) {
        this.keystoreMessage.set(this.unlockErrorMessage(code));
        this.recoveryKeyRequested.set(false);
        return;
      }
      this.bannerMessage.set(this.unlockErrorMessage(code));
    } catch (error) {
      this.bannerMessage.set(error instanceof TransportError ? error.message : this.unlockErrorMessage(undefined));
    } finally {
      this.submitting.set(false);
    }
  }

  async retryKeystore(): Promise<void> {
    if (this.retrying()) {
      return;
    }
    const run = ++this.retryRun;
    this.retrying.set(true);
    this.keystoreMessage.set(null);
    try {
      let response = await this.keyRing.retryKeystore();
      while (run === this.retryRun && response.outcome === 'Pending') {
        this.waiting.set(true);
        response = await this.keyRing.retryKeystore();
      }
      if (run === this.retryRun) {
        await this.applyRetry(response);
      }
    } catch (error) {
      if (run === this.retryRun) {
        this.keystoreMessage.set(error instanceof TransportError ? error.message : this.stillUnavailable());
      }
    } finally {
      if (run === this.retryRun) {
        this.retrying.set(false);
        this.waiting.set(false);
      }
    }
  }

  useRecoveryKey(): void {
    this.stopRetrying();
    this.keystoreMessage.set(null);
    this.recoveryKeyRequested.set(true);
  }

  returnToKeystore(): void {
    this.invalid.set(false);
    this.bannerMessage.set(null);
    this.recoveryKeyRequested.set(false);
  }

  dismissBanner(): void {
    this.bannerMessage.set(null);
  }

  dismissKeystoreMessage(): void {
    this.keystoreMessage.set(null);
  }

  private async applyRetry(response: RetryKeyRingKeystoreResponse): Promise<void> {
    const reason = RETRY_REASONS[response.outcome];
    if (response.outcome === 'Recovered') {
      this.result.set({ restartRequested: response.restartRequested, byRecoveryKey: false });
    } else if (reason) {
      this.retriedReason.set(reason);
    } else if (response.outcome === 'NotApplicable') {
      await this.keyRing.probe();
    } else {
      this.keystoreMessage.set(this.stillUnavailable());
    }
  }

  // A later answer belongs to a view the user has left, so bumping the run makes the loop drop it.
  private stopRetrying(): void {
    this.retryRun++;
    this.retrying.set(false);
    this.waiting.set(false);
  }

  private stillUnavailable(): string {
    return this.localization.translateKey(AppStrings.KeyRing.KeystoreRetry.StillUnavailable);
  }

  private unlockErrorMessage(code: string | undefined): string {
    const key = (code ? UNLOCK_ERROR_KEYS[code] : undefined) ?? AppStrings.Errors.KeyRing.KeystoreUnavailable;
    return this.localization.translateKey(key);
  }
}
