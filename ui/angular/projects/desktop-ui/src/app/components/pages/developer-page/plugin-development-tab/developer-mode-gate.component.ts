import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ButtonComponent, TranslatePipe } from '@shared';
import { SettingsModalService } from '../../../../services/settings-modal.service';

@Component({
  selector: 'app-developer-mode-gate',
  standalone: true,
  imports: [ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="developer-mode-gate">
      <span class="developer-mode-gate-icon icon icon-lock icon-2xl" aria-hidden="true"></span>
      <h2 class="developer-mode-gate-heading">
        {{ 'macrodeck.app:Developer.PluginDevelopment.GateHeading' | translate }}
      </h2>
      <p class="developer-mode-gate-message">
        {{ 'macrodeck.app:Developer.PluginDevelopment.GateMessage' | translate }}
      </p>
      <shared-button variant="primary" (click)="openDeveloperSettings()">
        {{ 'macrodeck.app:Developer.PluginDevelopment.GateAction' | translate }}
      </shared-button>
    </div>
  `,
  styles: [`
    :host {
      display: flex;
      flex-direction: column;
      flex: 1 1 auto;
      min-height: 0;
    }

    .developer-mode-gate {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--space-3);
      padding: var(--space-8) var(--space-4);
      text-align: center;
    }

    .developer-mode-gate-icon {
      color: var(--color-text-muted);
    }

    .developer-mode-gate-heading {
      font-size: var(--text-lg);
      font-weight: var(--font-bold);
      color: var(--color-text-primary);
    }

    .developer-mode-gate-message {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
      line-height: var(--leading-relaxed);
      max-width: 60ch;
    }
  `],
})
export class DeveloperModeGateComponent {
  private readonly settingsModal = inject(SettingsModalService);

  openDeveloperSettings(): void {
    this.settingsModal.open('developer');
  }
}
