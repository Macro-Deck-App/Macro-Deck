import { ChangeDetectionStrategy, Component, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, PluginAccessToken, Strings } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, ErrorBannerComponent, InputComponent, LocalizationService, ModalComponent, ToastService, TranslatePipe, dismissModal } from '@shared';
import { CopyValueComponent } from '../../../../../copy-value/copy-value.component';
import { EmptyStateComponent } from '../../../../../feedback/empty-state/empty-state.component';
import { SelectComponent, SelectOption } from '../../../../../forms/select/select.component';
import { ConfirmationModalComponent } from '../../../../../overlay/confirmation-modal/confirmation-modal.component';
import { PluginTokenService } from '../../../../../../services/plugin-token.service';

interface RevealedToken {
  name: string;
  plaintext: string;
}

@Component({
  selector: 'app-credentials-section',
  standalone: true,
  imports: [
    FormsModule,
    ButtonComponent,
    ButtonGroupComponent,
    ConfirmationModalComponent,
    CopyValueComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    InputComponent,
    ModalComponent,
    SelectComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './credentials-section.component.html',
  styleUrls: ['./credentials-section.component.scss'],
})
export class CredentialsSectionComponent {
  protected readonly tokenService = inject(PluginTokenService);
  private readonly toastService = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  readonly expiryOptions = computed<SelectOption[]>(() => [
    { value: '', label: this.localization.translateKey(AppStrings.Developer.Tokens.ExpiryNever) },
    { value: '30', label: this.localization.translateKey(AppStrings.Developer.Tokens.ExpiryDays, { count: 30 }) },
    { value: '90', label: this.localization.translateKey(AppStrings.Developer.Tokens.ExpiryDays, { count: 90 }) },
    { value: '365', label: this.localization.translateKey(AppStrings.Developer.Tokens.ExpiryDays, { count: 365 }) },
  ]);

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  readonly tokens = computed(() =>
    [...this.tokenService.tokens()].sort((a, b) => b.createdAt.localeCompare(a.createdAt)));

  readonly createModalOpen = signal(false);
  readonly createName = signal('');
  readonly createExpiry = signal('');
  readonly creating = signal(false);

  readonly revealed = signal<RevealedToken | null>(null);

  readonly revokeCandidate = signal<PluginAccessToken | null>(null);
  readonly removeCandidate = signal<PluginAccessToken | null>(null);

  openCreateModal(): void {
    this.createName.set('');
    this.createExpiry.set('');
    this.createModalOpen.set(true);
  }

  cancelCreateModal(): void {
    dismissModal(this.modal, () => {
      this.createModalOpen.set(false);
    });
  }

  async submitCreate(): Promise<void> {
    const name = this.createName().trim();
    if (!name || this.creating()) {
      return;
    }

    const expiryValue = this.createExpiry();
    const expiresInDays = expiryValue ? Number(expiryValue) : null;

    this.creating.set(true);
    try {
      const response = await this.tokenService.createToken({ name, expiresInDays });
      this.revealed.set({ name: response.token.name, plaintext: response.plaintext });
    } catch (error) {
      this.toastService.show(
        error instanceof Error ? error.message : this.localization.translateKey(AppStrings.Developer.Tokens.CreateFailed),
        { variant: 'error' });
    } finally {
      this.creating.set(false);
    }
  }

  acknowledgeReveal(): void {
    this.revealed.set(null);
    this.createModalOpen.set(false);
  }

  requestRevoke(token: PluginAccessToken): void {
    this.revokeCandidate.set(token);
  }

  cancelRevoke(): void {
    this.revokeCandidate.set(null);
  }

  async confirmRevoke(): Promise<void> {
    const token = this.revokeCandidate();
    if (!token) {
      return;
    }
    this.revokeCandidate.set(null);

    const response = await this.tokenService.revokeToken(token.id);
    if (!response.success) {
      this.toastService.show(
        response.error?.message ?? this.localization.translateKey(AppStrings.Developer.Tokens.RevokeTokenFailed),
        { variant: 'error' });
    }
  }

  requestRemove(token: PluginAccessToken): void {
    this.removeCandidate.set(token);
  }

  cancelRemove(): void {
    this.removeCandidate.set(null);
  }

  async confirmRemove(): Promise<void> {
    const token = this.removeCandidate();
    if (!token) {
      return;
    }
    this.removeCandidate.set(null);

    const response = await this.tokenService.deleteToken(token.id);
    if (!response.success) {
      this.toastService.show(
        response.error?.message ?? this.localization.translateKey(AppStrings.Developer.Tokens.RemoveFailed),
        { variant: 'error' });
    }
  }

  formatDate(iso: string | null | undefined): string {
    if (!iso) {
      return '-';
    }
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      return '-';
    }
    return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }

  expiryLabel(token: PluginAccessToken): string {
    return token.expiresAt
      ? this.formatDate(token.expiresAt)
      : this.localization.translateKey(AppStrings.Developer.Tokens.ExpiryNever);
  }

  scopesLabel(token: PluginAccessToken): string {
    return token.scopes.length > 0 ? token.scopes.join(', ') : this.localization.translateKey(Strings.Common.None);
  }
}
