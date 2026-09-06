import { ChangeDetectionStrategy, Component, Input, inject, signal, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, AuthScope } from '@macro-deck/runtime';
import { LocalizationService } from '../../../localization/localization.service';
import { TranslatePipe } from '../../../localization/translate.pipe';
import { AuthService } from '../../../services/auth.service';
import { InputComponent } from '../../forms/input/input.component';
import { ButtonComponent } from '../../button/button.component';
import { ErrorBannerComponent } from '../../feedback/error-banner/error-banner.component';

@Component({
  selector: 'shared-login-form',
  standalone: true,
  imports: [FormsModule, InputComponent, ButtonComponent, ErrorBannerComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login-form.component.html',
  styleUrls: ['./login-form.component.scss'],
})
export class LoginFormComponent {
  private readonly localization = inject(LocalizationService);

  private readonly auth = inject(AuthService);

  @Input({ required: true }) scope: AuthScope = 'client';

  readonly username = signal('');
  readonly password = signal('');
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly canSubmit = computed(() =>
    this.username().trim().length > 0 && this.password().length > 0 && !this.submitting());

  async submit(): Promise<void> {
    if (!this.canSubmit()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);
    try {
      const result = await this.auth.login(
        this.username().trim(),
        this.password(),
        this.scope,
      );
      if (!result.ok) {
        this.error.set(result.message ?? this.localization.translateKey(AppStrings.Auth.SignInFailed));
      }
    } finally {
      this.submitting.set(false);
    }
  }
}
