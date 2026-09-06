import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings, PendingPluginPairingRequest } from '@macro-deck/runtime';
import { ButtonComponent, CheckboxComponent, LocalizationService, ModalComponent, TranslatePipe } from '@shared';
import { PluginPairingService } from '../../../services/plugin-pairing.service';

@Component({
  selector: 'shared-plugin-pairing-dialog',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, CheckboxComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './plugin-pairing-dialog.component.html',
  styleUrls: ['./plugin-pairing-dialog.component.scss'],
})
export class PluginPairingDialogComponent implements OnDestroy {
  private readonly pairing = inject(PluginPairingService);
  private readonly localization = inject(LocalizationService);

  readonly pending = this.pairing.current;
  readonly queuedCount = this.pairing.queuedCount;

  private readonly now = signal(Date.now());
  private timer?: ReturnType<typeof setInterval>;

  readonly replaceConfirmed = signal(false);

  readonly busy = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly secondsRemaining = computed(() => {
    const request = this.pending();
    if (!request) {
      return 0;
    }
    const remaining = Math.floor((new Date(request.expiresAt).getTime() - this.now()) / 1000);
    return Math.max(0, remaining);
  });

  readonly approveDisabled = computed(() => {
    const request = this.pending();
    if (!request || this.busy()) {
      return true;
    }
    return request.replacesExistingRegistration && !this.replaceConfirmed();
  });

  constructor() {
    effect(() => {
      const request = this.pending();
      untracked(() => {
        this.replaceConfirmed.set(false);
        this.errorMessage.set(null);
        this.restartTimer(!!request);
      });
    });
  }

  ngOnDestroy(): void {
    this.stopTimer();
  }

  async approve(): Promise<void> {
    const request = this.pending();
    if (!request || this.approveDisabled()) {
      return;
    }
    this.busy.set(true);
    this.errorMessage.set(null);
    try {
      const succeeded = await this.pairing.approve(
        request.requestId,
        request.replacesExistingRegistration ? this.replaceConfirmed() : false,
      );
      if (!succeeded) {
        this.errorMessage.set(this.localization.translateKey(AppStrings.Dialogs.PluginPairing.ApproveFailed));
      }
    } catch {
      this.errorMessage.set(this.localization.translateKey(AppStrings.Dialogs.PluginPairing.ApproveFailed));
    } finally {
      this.busy.set(false);
    }
  }

  async reject(): Promise<void> {
    const request = this.pending();
    if (!request || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.errorMessage.set(null);
    try {
      const succeeded = await this.pairing.reject(request.requestId);
      if (!succeeded) {
        this.errorMessage.set(this.localization.translateKey(AppStrings.Dialogs.PluginPairing.RejectFailed));
      }
    } catch {
      this.errorMessage.set(this.localization.translateKey(AppStrings.Dialogs.PluginPairing.RejectFailed));
    } finally {
      this.busy.set(false);
    }
  }

  clientLine(request: PendingPluginPairingRequest, field: keyof NonNullable<PendingPluginPairingRequest['client']>): string {
    const value = request.client?.[field];
    return value === null || value === undefined || value === ''
      ? this.localization.translateKey(AppStrings.Dialogs.PluginPairing.NotReported)
      : String(value);
  }

  private restartTimer(active: boolean): void {
    this.stopTimer();
    if (!active) {
      return;
    }
    this.now.set(Date.now());
    this.timer = setInterval(() => {
      this.now.set(Date.now());
      const request = untracked(this.pending);
      if (request && this.secondsRemaining() <= 0) {
        this.pairing.expireLocally(request.requestId);
      }
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timer !== undefined) {
      clearInterval(this.timer);
      this.timer = undefined;
    }
  }
}
