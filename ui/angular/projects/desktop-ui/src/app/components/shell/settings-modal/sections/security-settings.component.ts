import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AppStrings,
  GetKeyRingProtectionResponse,
  GetLockScreenSettingsResponse,
} from '@macro-deck/runtime';
import {
  ApiService,
  AuthService,
  ButtonComponent,
  ErrorBannerComponent,
  InputComponent,
  LocalizationService,
  SettingsRowComponent,
  SettingsSectionComponent,
  ToggleSwitchComponent,
  TranslatePipe,
} from '@shared';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';
import { SettingsModalService } from '../../../../services/settings-modal.service';

const MIN_PASSWORD_LENGTH = 8;

const KEY_RING_BACKEND_KEYS: Record<string, string> = {
  None: AppStrings.Settings.Security.KeyRing.Backend.None,
  WindowsCredentialManager: AppStrings.Settings.Security.KeyRing.Backend.WindowsCredentialManager,
  MacOsKeychain: AppStrings.Settings.Security.KeyRing.Backend.MacOsKeychain,
  LinuxSecretService: AppStrings.Settings.Security.KeyRing.Backend.LinuxSecretService,
};

const KEY_RING_STATE_KEYS: Record<string, string> = {
  Unprotected: AppStrings.Settings.Security.KeyRing.State.Unprotected,
  Protected: AppStrings.Settings.Security.KeyRing.State.Protected,
  Locked: AppStrings.Settings.Security.KeyRing.State.Locked,
};

const KEY_RING_UNPROTECTED_REASON_KEYS: Record<string, string> = {
  RecoveryKeyNotExported: AppStrings.Settings.Security.KeyRing.Unprotected.RecoveryKeyNotExported,
  NoKeystore: AppStrings.Settings.Security.KeyRing.Unprotected.NoKeystore,
  Portable: AppStrings.Settings.Security.KeyRing.Unprotected.Portable,
};

@Component({
  selector: 'app-security-settings',
  standalone: true,
  imports: [
    FormsModule, InputComponent, ButtonComponent, ConfirmationModalComponent, ErrorBannerComponent,
    SettingsSectionComponent, SettingsRowComponent, ToggleSwitchComponent, TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './security-settings.component.html',
  styleUrls: ['./security-settings.component.scss'],
})
export class SecuritySettingsComponent {
  private readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly settingsModal = inject(SettingsModalService);

  readonly currentUsername = this.auth.username;

  readonly canSignOut = computed(() => this.auth.state() === 'authenticated' && !this.auth.trusted());

  readonly confirmingSignOut = signal(false);
  readonly signOutBusy = signal(false);

  readonly lockScreenEnabled = signal(false);
  readonly lockScreenLoaded = signal(false);
  readonly lockScreenBusy = signal(false);

  readonly keyRingProtection = signal<GetKeyRingProtectionResponse | null>(null);

  readonly usernamePassword = signal('');
  readonly newUsername = signal('');
  readonly usernameSubmitting = signal(false);
  readonly usernameError = signal<string | null>(null);
  readonly usernameChanged = signal(false);

  readonly currentPassword = signal('');
  readonly newPassword = signal('');
  readonly newPasswordConfirm = signal('');
  readonly passwordSubmitting = signal(false);
  readonly passwordError = signal<string | null>(null);
  readonly passwordChanged = signal(false);

  readonly newPasswordTooShort = computed(() =>
    this.newPassword().length > 0 && this.newPassword().length < MIN_PASSWORD_LENGTH);

  readonly newPasswordsMismatch = computed(() =>
    this.newPasswordConfirm().length > 0 && this.newPassword() !== this.newPasswordConfirm());

  readonly canSubmitUsername = computed(() =>
    this.newUsername().trim().length > 0 && this.usernamePassword().length > 0 && !this.usernameSubmitting());

  readonly canSubmitPassword = computed(() =>
    this.currentPassword().length > 0
    && this.newPassword().length >= MIN_PASSWORD_LENGTH
    && this.newPassword() === this.newPasswordConfirm()
    && !this.passwordSubmitting());

  constructor() {
    void this.loadLockScreenSettings();
    void this.loadKeyRingProtection();
  }

  protected backendLabel(backend: string): string {
    const key = KEY_RING_BACKEND_KEYS[backend];
    return key ? this.localization.translateKey(key) : '';
  }

  protected stateLabel(state: string): string {
    const key = KEY_RING_STATE_KEYS[state];
    return key ? this.localization.translateKey(key) : '';
  }

  protected unprotectedReasonMessage(protection: GetKeyRingProtectionResponse): string | null {
    if (protection.state !== 'Unprotected') {
      return null;
    }
    const key = KEY_RING_UNPROTECTED_REASON_KEYS[protection.unprotectedReason];
    return key ? this.localization.translateKey(key) : null;
  }

  async setLockScreenEnabled(value: boolean): Promise<void> {
    if (this.lockScreenBusy()) {
      return;
    }
    this.lockScreenBusy.set(true);
    this.lockScreenEnabled.set(value);
    try {
      const response = await this.api.updateLockScreenSettings({ enabled: value });
      this.lockScreenEnabled.set(response.enabled);
    } catch {
      await this.loadLockScreenSettings();
    } finally {
      this.lockScreenBusy.set(false);
    }
  }

  private async loadLockScreenSettings(): Promise<void> {
    try {
      this.applyLockScreenState(await this.api.getLockScreenSettings());
    } catch {
    } finally {
      this.lockScreenLoaded.set(true);
    }
  }

  private applyLockScreenState(state: GetLockScreenSettingsResponse): void {
    this.lockScreenEnabled.set(state.enabled);
  }

  private async loadKeyRingProtection(): Promise<void> {
    try {
      this.keyRingProtection.set(await this.api.getKeyRingProtection());
    } catch (error) {
      console.error('Failed to load the key ring protection state:', error);
    }
  }

  async submitUsername(): Promise<void> {
    if (!this.canSubmitUsername()) {
      return;
    }

    this.usernameSubmitting.set(true);
    this.usernameError.set(null);
    this.usernameChanged.set(false);
    try {
      const result = await this.auth.changeUsername(this.usernamePassword(), this.newUsername().trim());
      if (result.ok) {
        this.usernameChanged.set(true);
        this.usernamePassword.set('');
        this.newUsername.set('');
      } else {
        this.usernameError.set(
          result.message ?? this.localization.translateKey(AppStrings.Settings.Security.UsernameChangeFailed),
        );
      }
    } finally {
      this.usernameSubmitting.set(false);
    }
  }

  async submitPassword(): Promise<void> {
    if (!this.canSubmitPassword()) {
      return;
    }

    this.passwordSubmitting.set(true);
    this.passwordError.set(null);
    this.passwordChanged.set(false);
    try {
      const result = await this.auth.changePassword(this.currentPassword(), this.newPassword());
      if (result.ok) {
        this.passwordChanged.set(true);
        this.currentPassword.set('');
        this.newPassword.set('');
        this.newPasswordConfirm.set('');
      } else {
        this.passwordError.set(
          result.message ?? this.localization.translateKey(AppStrings.Settings.Security.PasswordChangeFailed),
        );
      }
    } finally {
      this.passwordSubmitting.set(false);
    }
  }

  requestSignOut(): void {
    this.confirmingSignOut.set(true);
  }

  cancelSignOut(): void {
    this.confirmingSignOut.set(false);
  }

  async confirmSignOut(): Promise<void> {
    this.confirmingSignOut.set(false);
    this.signOutBusy.set(true);
    try {
      await this.auth.logout();
    } finally {
      this.signOutBusy.set(false);
      // The shell swaps the whole app for the login form; leaving the dialog open would strand it
      // over that form.
      this.settingsModal.close();
    }
  }
}
