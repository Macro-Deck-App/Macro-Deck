import { ChangeDetectionStrategy, Component, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, ErrorBannerComponent, InputComponent, LocalizationService, ModalComponent, SettingsSectionComponent, ToastService, TranslatePipe, dismissModal } from '@shared';
import { CopyValueComponent } from '../../../copy-value/copy-value.component';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';
import { McpSettingsService } from '../../../../services/mcp-settings.service';
import { McpProviderPolicy, McpRiskMode, McpToken } from '../../../../shared/transport/mcp-messages';

interface RevealedToken {
  name: string;
  plaintext: string;
}

@Component({
  selector: 'app-ai-clients-settings',
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
    SettingsSectionComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ai-clients-settings.component.html',
  styleUrls: ['./ai-clients-settings.component.scss'],
})
export class AiClientsSettingsComponent {
  readonly mcp = inject(McpSettingsService);
  private readonly toast = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  readonly policyOptions = computed<SelectOption[]>(() => [
    { value: '', label: '' },
    { value: 'Allow', label: this.localization.translateKey(AppStrings.Settings.Mcp.PolicyAllow) },
    { value: 'Confirm', label: this.localization.translateKey(AppStrings.Settings.Mcp.PolicyConfirm) },
    { value: 'Off', label: this.localization.translateKey(AppStrings.Settings.Mcp.PolicyOff) },
  ]);

  readonly createModalOpen = signal(false);
  readonly createName = signal('');
  readonly creating = signal(false);
  readonly revealed = signal<RevealedToken | null>(null);
  readonly revokeCandidate = signal<McpToken | null>(null);
  readonly removeCandidate = signal<McpToken | null>(null);

  readonly configSnippet = computed(() => {
    const token = this.revealed();
    const url = this.mcp.endpoint()?.url;
    if (!token || !url) {
      return '';
    }
    return JSON.stringify({
      mcpServers: {
        'macro-deck': { type: 'http', url, headers: { Authorization: `Bearer ${token.plaintext}` } },
      },
    }, null, 2);
  });

  constructor() {
    void this.mcp.load();
  }

  refresh(): Promise<void> {
    return this.mcp.load();
  }

  openCreateModal(): void {
    this.createName.set('');
    this.revealed.set(null);
    this.createModalOpen.set(true);
  }

  cancelCreateModal(): void {
    dismissModal(this.modal, () => {
      this.createModalOpen.set(false);
      this.revealed.set(null);
    });
  }

  async submitCreate(): Promise<void> {
    const name = this.createName().trim();
    if (!name || this.creating()) {
      return;
    }

    this.creating.set(true);
    try {
      const response = await this.mcp.createToken({ name });
      if (response.success && response.token && response.plaintext) {
        this.revealed.set({ name: response.token.name, plaintext: response.plaintext });
      } else {
        this.showError(response.error?.message, AppStrings.Developer.Tokens.CreateFailed);
      }
    } catch {
      this.showError(undefined, AppStrings.Developer.Tokens.CreateFailed);
    } finally {
      this.creating.set(false);
    }
  }

  acknowledgeReveal(): void {
    this.revealed.set(null);
    this.createModalOpen.set(false);
  }

  requestRevoke(token: McpToken): void {
    this.revokeCandidate.set(token);
  }

  cancelRevoke(): void {
    this.revokeCandidate.set(null);
  }

  async confirmRevoke(): Promise<void> {
    const token = this.revokeCandidate();
    this.revokeCandidate.set(null);
    if (!token) {
      return;
    }
    const response = await this.mcp.revokeToken(token.id);
    if (!response.success) {
      this.showError(response.error?.message, AppStrings.Developer.Tokens.RevokeTokenFailed);
    }
  }

  requestRemove(token: McpToken): void {
    this.removeCandidate.set(token);
  }

  cancelRemove(): void {
    this.removeCandidate.set(null);
  }

  async confirmRemove(): Promise<void> {
    const token = this.removeCandidate();
    this.removeCandidate.set(null);
    if (!token) {
      return;
    }
    const response = await this.mcp.deleteToken(token.id);
    if (!response.success) {
      this.showError(response.error?.message, AppStrings.Developer.Tokens.RemoveFailed);
    }
  }

  providerName(provider: McpProviderPolicy): string {
    return resolveLocalizedText(provider.name, this.localization) || provider.integrationId;
  }

  confirmedActionNames(provider: McpProviderPolicy): string {
    return provider.confirmedActions
      .map(name => resolveLocalizedText(name, this.localization))
      .join(', ');
  }

  optionsFor(provider: McpProviderPolicy): SelectOption[] {
    const [, ...modes] = this.policyOptions();
    const defaultMode = modes.find(option => option.value === provider.defaultMode)?.label ?? provider.defaultMode;
    return [
      { value: '', label: this.localization.translateKey(AppStrings.Settings.Mcp.PolicyDefault, { mode: defaultMode }) },
      ...modes,
    ];
  }

  async changePolicy(provider: McpProviderPolicy, value: string): Promise<void> {
    const mode = value === '' ? null : value as McpRiskMode;
    try {
      const response = await this.mcp.setPolicy(provider.integrationId, mode);
      if (!response.success) {
        this.showError(response.error?.message, AppStrings.Settings.Mcp.PolicySaveFailed);
      }
    } catch {
      this.showError(undefined, AppStrings.Settings.Mcp.PolicySaveFailed);
    }
  }

  formatDate(iso: string | null | undefined): string {
    if (!iso) {
      return this.localization.translateKey(AppStrings.Settings.Mcp.NeverUsed);
    }
    const date = new Date(iso);
    return Number.isNaN(date.getTime())
      ? '-'
      : date.toLocaleString(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  clientLabel(name: string | null | undefined, version: string | null | undefined): string {
    if (!name) {
      return this.localization.translateKey(AppStrings.Settings.Mcp.UnknownClient);
    }
    return version ? `${name} ${version}` : name;
  }

  private showError(message: unknown, fallbackKey: string): void {
    const text = resolveLocalizedText(message as Parameters<typeof resolveLocalizedText>[0], this.localization);
    this.toast.show(text || this.localization.translateKey(fallbackKey), { variant: 'error' });
  }
}
