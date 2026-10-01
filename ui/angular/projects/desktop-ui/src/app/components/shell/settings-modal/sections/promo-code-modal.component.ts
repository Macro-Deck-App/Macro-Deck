import { ChangeDetectionStrategy, Component, ViewChild, computed, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, CompanionPromoCodeStatus } from '@macro-deck/runtime';
import {
  ApiService,
  ButtonComponent,
  InputComponent,
  LocalizationService,
  ModalComponent,
  TranslatePipe,
  dismissModal,
} from '@shared';
import { ConnectAccountService } from '../../../../services/connect-account.service';

const RESULT_KEYS: Record<CompanionPromoCodeStatus, string> = {
  redeemed: AppStrings.Settings.License.Redeem.Result.Redeemed,
  accountLicenseExists: AppStrings.Settings.License.Redeem.Result.AccountLicenseExists,
  alreadyLicensed: AppStrings.Settings.License.Redeem.Result.AlreadyLicensed,
  invalid: AppStrings.Settings.License.Redeem.Result.Invalid,
  expired: AppStrings.Settings.License.Redeem.Result.Expired,
  alreadyRedeemed: AppStrings.Settings.License.Redeem.Result.AlreadyRedeemed,
  revoked: AppStrings.Settings.License.Redeem.Result.Revoked,
  accountSuspended: AppStrings.Settings.License.Redeem.Result.AccountSuspended,
  rateLimited: AppStrings.Settings.License.Redeem.Result.RateLimited,
  unavailable: AppStrings.Settings.License.Redeem.Result.Unavailable,
  signedOut: AppStrings.Settings.License.Redeem.Result.SignedOut,
};

const SUCCESS: ReadonlySet<CompanionPromoCodeStatus> = new Set(['redeemed', 'accountLicenseExists']);

interface RedeemOutcome {
  message: string;
  success: boolean;
}

@Component({
  selector: 'app-promo-code-modal',
  standalone: true,
  imports: [FormsModule, ModalComponent, ButtonComponent, InputComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './promo-code-modal.component.html',
  styleUrls: ['./promo-code-modal.component.scss'],
})
export class PromoCodeModalComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly connect = inject(ConnectAccountService);

  readonly closed = output<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  readonly signedIn = this.connect.isSignedIn;
  readonly code = signal('');
  readonly redeeming = signal(false);
  readonly outcome = signal<RedeemOutcome | null>(null);

  readonly canRedeem = computed(
    () => this.signedIn() && this.code().trim().length > 0 && !this.redeeming() && !this.outcome()?.success,
  );

  async redeem(): Promise<void> {
    if (!this.canRedeem()) {
      return;
    }
    this.redeeming.set(true);
    this.outcome.set(null);
    try {
      const response = await this.api.redeemCompanionPromoCode(this.code().trim());
      this.outcome.set({
        message: this.message(response.status, response.retryAfterSeconds),
        success: SUCCESS.has(response.status),
      });
    } catch {
      this.outcome.set({
        message: this.localization.translateKey(AppStrings.Settings.License.Redeem.Result.Failed),
        success: false,
      });
    } finally {
      this.redeeming.set(false);
    }
  }

  close(): void {
    dismissModal(this.modal, () => this.closed.emit());
  }

  private message(status: CompanionPromoCodeStatus, retryAfterSeconds: number | null): string {
    if (status === 'rateLimited') {
      const time = retryAfterSeconds != null ? this.formatTime(Date.now() + retryAfterSeconds * 1000) : null;
      return time
        ? this.localization.translateKey(AppStrings.Settings.License.Redeem.Result.RateLimited, { time })
        : this.localization.translateKey(AppStrings.Settings.License.Redeem.Result.RateLimitedSoon);
    }
    return this.localization.translateKey(RESULT_KEYS[status]);
  }

  private formatTime(value: number): string {
    const { locale, hourCycle } = this.localization.timeLocale();
    return new Date(value).toLocaleString(locale, { hourCycle });
  }
}
