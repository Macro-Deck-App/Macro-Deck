import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, ErrorBannerComponent, LocalizationService, SettingsSectionComponent, ToastService, TranslatePipe } from '@shared';
import { AvatarComponent } from '../../../account/avatar.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';
import { ConnectAccountService } from '../../../../services/connect-account.service';
import { ExternalLinkService } from '../../../../services/external-link.service';
import { TextClipboardService, clipboardFailureDetail } from '../../../../services/text-clipboard.service';

const SIGN_IN_FAILURE_KEYS = {
  denied: AppStrings.Settings.Account.SignInDenied,
  expired: AppStrings.Settings.Account.SignInExpired,
  unreachable: AppStrings.Settings.Account.SignInUnreachable,
  failed: AppStrings.Settings.Account.SignInFailed,
} as const;

@Component({
  selector: 'app-account-settings',
  standalone: true,
  imports: [
    AvatarComponent,
    ButtonComponent,
    ButtonGroupComponent,
    ConfirmationModalComponent,
    ErrorBannerComponent,
    LoadingStateComponent,
    SettingsSectionComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './account-settings.component.html',
  styleUrls: ['./account-settings.component.scss'],
})
export class AccountSettingsComponent implements OnInit {
  protected readonly connect = inject(ConnectAccountService);
  private readonly externalLink = inject(ExternalLinkService);
  private readonly localization = inject(LocalizationService);
  private readonly clipboard = inject(TextClipboardService);
  private readonly toast = inject(ToastService);

  readonly signInBusy = signal(false);
  readonly cancelBusy = signal(false);
  readonly signOutBusy = signal(false);
  readonly confirmingSignOut = signal(false);

  protected readonly account = computed(() => this.connect.session()?.account ?? null);
  protected readonly status = computed(() => this.connect.session()?.status ?? 'signedOut');

  protected readonly initialLoad = computed(() => this.connect.isLoading() && !this.connect.session());

  protected readonly prompt = this.connect.signInPrompt;

  private readonly now = signal(Date.now());

  protected dateTimeLabel(value: string | number | Date): string {
    const { locale, hourCycle } = this.localization.timeLocale();
    return new Date(value).toLocaleString(locale, { dateStyle: 'medium', timeStyle: 'medium', hourCycle });
  }

  protected readonly remaining = computed(() => {
    const pending = this.prompt();
    if (!pending) {
      return null;
    }

    const seconds = Math.floor((Date.parse(pending.expiresAtUtc) - this.now()) / 1000);
    if (!Number.isFinite(seconds) || seconds <= 0) {
      return null;
    }

    return `${Math.floor(seconds / 60)}:${`${seconds % 60}`.padStart(2, '0')}`;
  });

  protected readonly signInFailureMessage = computed(() => {
    const failure = this.connect.session()?.signInFailure;
    if (!failure) {
      return null;
    }

    return this.localization.translateKey(SIGN_IN_FAILURE_KEYS[failure]);
  });

  protected readonly offlineSince = computed(() => {
    const session = this.connect.session();
    return session?.connectivity === 'offline' ? session.offlineSince : null;
  });

  protected readonly attentionMessage = computed(() =>
    this.connect.session()?.message ?? this.localization.translateKey(AppStrings.Settings.Account.DefaultSuspensionMessage));

  constructor() {
    const ticker = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(ticker));
  }

  async ngOnInit(): Promise<void> {
    await this.connect.load();
  }

  async copyCode(): Promise<void> {
    const pending = this.prompt();
    if (!pending) {
      return;
    }

    const result = await this.clipboard.copyText(pending.userCode);
    this.toast.show(
      result.status === 'copied'
        ? this.localization.translateKey(AppStrings.Settings.Account.CodeCopied)
        : clipboardFailureDetail(result.reason, this.localization),
      { variant: result.status === 'copied' ? 'success' : 'error' },
    );
  }

  async signIn(): Promise<void> {
    if (this.signInBusy()) {
      return;
    }
    this.signInBusy.set(true);
    try {
      await this.connect.signIn();
    } finally {
      this.signInBusy.set(false);
    }
  }

  async cancelSignIn(): Promise<void> {
    if (this.cancelBusy()) {
      return;
    }
    this.cancelBusy.set(true);
    try {
      await this.connect.cancelSignIn();
    } finally {
      this.cancelBusy.set(false);
    }
  }

  manageAccount(): void {
    const url = this.connect.session()?.accountManagementUrl;
    if (url) {
      this.externalLink.open(url);
    }
  }

  requestSignOut(): void {
    this.confirmingSignOut.set(true);
  }

  cancelSignOutConfirm(): void {
    this.confirmingSignOut.set(false);
  }

  async confirmSignOut(): Promise<void> {
    this.confirmingSignOut.set(false);
    this.signOutBusy.set(true);
    try {
      await this.connect.signOut();
    } finally {
      this.signOutBusy.set(false);
    }
  }
}
