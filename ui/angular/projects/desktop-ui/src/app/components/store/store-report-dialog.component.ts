import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AppStrings,
  StoreEntryReportCategory,
  StoreExtensionKind,
  StoreReportResponse,
  StoreReviewReportCategory,
  StoreReviewWriteError,
} from '@macro-deck/runtime';
import {
  ApiService,
  ButtonComponent,
  InputComponent,
  LocalizationService,
  ModalComponent,
  ToastService,
  TranslatePipe,
} from '@shared';
import { ConnectAccountService } from '../../services/connect-account.service';

export const STORE_REPORT_DETAIL_MAX = 1000;

export const STORE_ENTRY_REPORT_REASONS: readonly StoreEntryReportCategory[] =
  ['InappropriateContent', 'Misleading', 'Impersonation', 'Malicious', 'Spam', 'Other'];

export const STORE_REVIEW_REPORT_REASONS: readonly StoreReviewReportCategory[] = ['Spam', 'Abuse', 'OffTopic', 'Other'];

export type StoreReportTarget =
  | { type: 'entry'; name: string }
  | { type: 'review'; reviewId: string };

type ReportReason = StoreEntryReportCategory | StoreReviewReportCategory;

const OTHER: ReportReason = 'Other';
const REASON_KEYS: Record<ReportReason, string> = {
  InappropriateContent: AppStrings.Store.Report.Reason.InappropriateContent,
  Misleading: AppStrings.Store.Report.Reason.Misleading,
  Impersonation: AppStrings.Store.Report.Reason.Impersonation,
  Malicious: AppStrings.Store.Report.Reason.Malicious,
  Spam: AppStrings.Store.Report.Reason.Spam,
  Abuse: AppStrings.Store.Report.Reason.Abuse,
  OffTopic: AppStrings.Store.Report.Reason.OffTopic,
  Other: AppStrings.Store.Report.Reason.Other,
};

@Component({
  selector: 'app-store-report-dialog',
  standalone: true,
  imports: [FormsModule, ButtonComponent, InputComponent, ModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-report-dialog.component.html',
  styleUrls: ['./store-report-dialog.component.scss'],
})
export class StoreReportDialogComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly account = inject(ConnectAccountService);
  private readonly toasts = inject(ToastService);
  private destroyed = false;

  readonly extensionKind = input.required<StoreExtensionKind>();
  readonly extensionId = input.required<string>();
  readonly target = input.required<StoreReportTarget>();

  readonly closed = output<void>();
  readonly reported = output<void>();

  constructor() {
    inject(DestroyRef).onDestroy(() => this.destroyed = true);
  }

  protected readonly reason = signal<ReportReason | null>(null);
  protected readonly detail = signal('');
  protected readonly submitted = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly suspended = computed(() => this.account.session()?.status === 'suspended');

  protected readonly heading = computed(() => {
    const target = this.target();
    return target.type === 'entry'
      ? this.localization.translateKey(AppStrings.Store.Report.EntryHeading, { name: target.name })
      : this.localization.translateKey(AppStrings.Store.Report.ReviewHeading);
  });

  protected readonly reasons = computed(() =>
    (this.target().type === 'entry' ? STORE_ENTRY_REPORT_REASONS : STORE_REVIEW_REPORT_REASONS)
      .map(value => ({ value, label: this.localization.translateKey(REASON_KEYS[value]) })));

  protected readonly detailRequired = computed(() => this.reason() === OTHER);

  protected readonly detailCounter = computed(() => this.localization.translateKey(
    AppStrings.Store.Reviews.BodyCounter, { length: codePoints(this.detail().trim()), max: STORE_REPORT_DETAIL_MAX }));

  protected readonly validationErrors = computed(() => {
    const strings = AppStrings.Store.Report.Validation;
    const errors: { reason?: string; detail?: string } = {};
    if (this.reason() === null) {
      errors.reason = this.localization.translateKey(strings.ReasonRequired);
    }

    const detail = this.detail().trim();
    if (detail.length === 0 && this.detailRequired()) {
      errors.detail = this.localization.translateKey(strings.DetailRequired);
    } else if (codePoints(detail) > STORE_REPORT_DETAIL_MAX) {
      errors.detail = this.localization.translateKey(strings.DetailTooLong);
    } else if (hasDisallowedControl(detail)) {
      errors.detail = this.localization.translateKey(strings.DetailInvalid);
    }
    return errors;
  });

  protected readonly visibleErrors = computed(() => this.submitted() ? this.validationErrors() : {});

  protected selectReason(value: ReportReason): void {
    if (!this.busy()) {
      this.reason.set(value);
    }
  }

  protected close(): void {
    if (!this.busy()) {
      this.closed.emit();
    }
  }

  protected async submit(): Promise<void> {
    this.submitted.set(true);
    this.error.set(null);
    const errors = this.validationErrors();
    if (errors.reason || errors.detail || this.busy() || this.suspended()) {
      return;
    }

    const request = { category: this.reason()!, detail: this.detail().trim() || null };
    const target = this.target();
    this.busy.set(true);
    let response: StoreReportResponse;
    try {
      response = target.type === 'entry'
        ? await this.api.reportStoreEntry(this.extensionKind(), this.extensionId(), request)
        : await this.api.reportStoreReview(this.extensionKind(), this.extensionId(), target.reviewId, request);
    } catch (error) {
      console.error('Failed to send the store report:', error);
      response = { success: false, error: null };
    } finally {
      this.busy.set(false);
    }

    if (response.success || response.error?.code === 'already_reported') {
      this.toasts.show(this.localization.translateKey(response.success
        ? AppStrings.Store.Report.Received
        : AppStrings.Store.Report.Error.AlreadyReported));
      if (!this.destroyed) {
        this.reported.emit();
      }
    } else if (!this.destroyed) {
      this.error.set(this.errorMessage(response.error ?? null));
    }
  }

  private errorMessage(error: StoreReviewWriteError | null): string {
    const strings = AppStrings.Store.Report.Error;
    const seconds = error?.retryAfterSeconds;
    const hasSeconds = typeof seconds === 'number' && seconds > 0;
    switch (error?.code) {
      case 'sign_in_required':
        return this.localization.translateKey(AppStrings.Store.Reviews.Error.SignInRequired);
      case 'account_suspended':
        return this.localization.translateKey(AppStrings.Store.Reviews.Error.AccountSuspended);
      case 'forbidden':
        return this.localization.translateKey(strings.NotAllowed);
      case 'gone':
        return this.localization.translateKey(strings.Gone);
      case 'not_found':
        return this.localization.translateKey(strings.NotFound);
      case 'report_unavailable':
        return this.localization.translateKey(strings.Unavailable);
      case 'cooldown':
        return hasSeconds
          ? this.localization.translateKey(strings.CooldownSeconds, { count: Math.ceil(seconds) })
          : this.localization.translateKey(strings.Cooldown);
      case 'retry_later':
        return hasSeconds
          ? this.localization.translateKey(strings.RetryLaterSeconds, { count: Math.ceil(seconds) })
          : this.localization.translateKey(strings.RetryLater);
      case 'validation':
        return this.localization.translateKey(strings.Validation);
      default:
        return this.localization.translateKey(strings.PlatformUnavailable);
    }
  }
}

function codePoints(value: string): number {
  return [...value].length;
}

function hasDisallowedControl(value: string): boolean {
  return [...value].some(character => character < ' ' && character !== '\t' && character !== '\r' && character !== '\n');
}
