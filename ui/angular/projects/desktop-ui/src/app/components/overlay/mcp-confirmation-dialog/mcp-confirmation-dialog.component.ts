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

import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, CheckboxComponent, LocalizationService, ModalComponent, TranslatePipe } from '@shared';
import { McpConfirmationService } from '../../../services/mcp-confirmation.service';

@Component({
  selector: 'shared-mcp-confirmation-dialog',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, CheckboxComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './mcp-confirmation-dialog.component.html',
  styleUrls: ['./mcp-confirmation-dialog.component.scss'],
})
export class McpConfirmationDialogComponent implements OnDestroy {
  private readonly confirmations = inject(McpConfirmationService);
  private readonly localization = inject(LocalizationService);

  readonly pending = this.confirmations.current;
  readonly alwaysAllow = signal(false);
  readonly busy = signal(false);
  readonly errorMessage = signal<string | null>(null);

  private readonly pendingId = computed(() => this.pending()?.id ?? null);
  private expiryTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => {
      const id = this.pendingId();
      untracked(() => {
        this.alwaysAllow.set(false);
        this.errorMessage.set(null);
        this.scheduleExpiry(id ?? undefined, this.pending()?.expiresAt);
      });
    });
  }

  ngOnDestroy(): void {
    this.clearExpiry();
  }

  allow(): Promise<void> {
    const request = this.pending();
    const alwaysAllow = !!request?.alwaysAllowable && this.alwaysAllow();
    return this.answer(request?.id, id => this.confirmations.allow(id, alwaysAllow));
  }

  decline(): Promise<void> {
    return this.answer(this.pending()?.id, id => this.confirmations.decline(id));
  }

  private async answer(id: string | undefined, send: (id: string) => Promise<boolean>): Promise<void> {
    if (!id || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.errorMessage.set(null);
    try {
      if (!await send(id)) {
        this.errorMessage.set(this.localization.translateKey(AppStrings.Mcp.Confirmation.AnswerFailed));
      }
    } catch {
      this.errorMessage.set(this.localization.translateKey(AppStrings.Mcp.Confirmation.AnswerFailed));
    } finally {
      this.busy.set(false);
    }
  }

  private scheduleExpiry(id: string | undefined, expiresAt: string | undefined): void {
    this.clearExpiry();
    if (!id || !expiresAt) {
      return;
    }
    const remaining = Math.max(0, new Date(expiresAt).getTime() - Date.now());
    this.expiryTimer = setTimeout(() => this.confirmations.expireLocally(id), remaining);
  }

  private clearExpiry(): void {
    if (this.expiryTimer !== undefined) {
      clearTimeout(this.expiryTimer);
      this.expiryTimer = undefined;
    }
  }
}
