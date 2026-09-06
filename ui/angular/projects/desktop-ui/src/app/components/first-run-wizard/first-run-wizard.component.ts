import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AppStrings,
} from '@macro-deck/runtime';
import {
  AuthService,
  ButtonComponent,
  ErrorBannerComponent,
  InputComponent,
  LocalizationService,
  TranslatePipe,
} from '@shared';
import { MigrationOfferService } from '../../services/migration-offer.service';

const MIN_PASSWORD_LENGTH = 8;

@Component({
  selector: 'app-first-run-wizard',
  standalone: true,
  imports: [FormsModule, InputComponent, ButtonComponent, ErrorBannerComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './first-run-wizard.component.html',
  styleUrls: ['./first-run-wizard.component.scss'],
})
export class FirstRunWizardComponent {
  private readonly auth = inject(AuthService);
  private readonly localization = inject(LocalizationService);
  private readonly migrationOffer = inject(MigrationOfferService);
  protected readonly appStrings = AppStrings;

  readonly username = signal('');
  readonly password = signal('');
  readonly passwordConfirm = signal('');
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly passwordTooShort = computed(() =>
    this.password().length > 0 && this.password().length < MIN_PASSWORD_LENGTH);

  readonly passwordsMismatch = computed(() =>
    this.passwordConfirm().length > 0 && this.password() !== this.passwordConfirm());

  readonly canSubmit = computed(() =>
    this.username().trim().length > 0
    && this.password().length >= MIN_PASSWORD_LENGTH
    && this.password() === this.passwordConfirm()
    && !this.submitting());

  async submit(): Promise<void> {
    if (!this.canSubmit()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);
    try {
      // Armed before the call, not after: auth.setup() flips the auth state, and the state switch
      // in app.component.html tears this component down mid-await the moment it does, so anything
      // this component tried to do afterwards would never run.
      this.migrationOffer.arm();
      const result = await this.auth.setup(this.username().trim(), this.password());
      if (!result.ok) {
        this.error.set(result.message ?? this.localization.translateKey(AppStrings.Setup.Account.SetupFailed));
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
