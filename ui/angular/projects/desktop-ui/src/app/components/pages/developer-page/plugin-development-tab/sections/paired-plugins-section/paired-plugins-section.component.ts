import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AppStrings, PairedPlugin } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../../../feedback/empty-state/empty-state.component';
import { ConfirmationModalComponent } from '../../../../../overlay/confirmation-modal/confirmation-modal.component';
import { PluginPairingService } from '../../../../../../services/plugin-pairing.service';

@Component({
  selector: 'app-paired-plugins-section',
  standalone: true,
  imports: [
    ButtonComponent,
    ConfirmationModalComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './paired-plugins-section.component.html',
  styleUrls: ['./paired-plugins-section.component.scss'],
})
export class PairedPluginsSectionComponent {
  protected readonly pairingService = inject(PluginPairingService);
  private readonly toastService = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  readonly pairedPlugins = computed(() =>
    [...this.pairingService.pairedPlugins()].sort((a, b) => b.createdAt.localeCompare(a.createdAt)));

  readonly revokePairedCandidate = signal<PairedPlugin | null>(null);

  requestRevokePaired(plugin: PairedPlugin): void {
    this.revokePairedCandidate.set(plugin);
  }

  cancelRevokePaired(): void {
    this.revokePairedCandidate.set(null);
  }

  async confirmRevokePaired(): Promise<void> {
    const plugin = this.revokePairedCandidate();
    if (!plugin) {
      return;
    }
    this.revokePairedCandidate.set(null);

    try {
      await this.pairingService.revoke(plugin.pluginId);
    } catch (error) {
      this.toastService.show(
        error instanceof Error
          ? error.message
          : this.localization.translateKey(AppStrings.Developer.Tokens.RevokePluginFailed),
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
}
