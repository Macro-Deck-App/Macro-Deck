import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ButtonComponent, ModalComponent, TranslatePipe } from '@shared';
import { PluginCompanionConsentService } from '../../../services/plugin-companion-consent.service';

@Component({
  selector: 'shared-plugin-companion-consent-dialog',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './plugin-companion-consent-dialog.component.html',
  styleUrls: ['./plugin-companion-consent-dialog.component.scss'],
})
export class PluginCompanionConsentDialogComponent {
  private readonly consent = inject(PluginCompanionConsentService);

  readonly pending = this.consent.pending;
  readonly busy = computed(() => this.consent.allowing() !== null);

  async allow(): Promise<void> {
    const request = this.pending();
    if (!request || this.busy()) {
      return;
    }
    await this.consent.allow(request.id);
  }

  notNow(): void {
    const request = this.pending();
    if (request && !this.busy()) {
      this.consent.defer(request.id);
    }
  }
}
