import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { ApiService, AuthService, TranslatePipe } from '@shared';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { MigrationOfferService } from '../../../services/migration-offer.service';
import { MigrationService } from '../../../services/migration.service';
import { MigrationSourceChoice, MigrationWizardService } from '../../../services/migration-wizard.service';
import { OnboardingService } from '../../../services/onboarding.service';

@Component({
  selector: 'app-migration-offer-modal',
  standalone: true,
  imports: [ConfirmationModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (offer.visible()) {
      <shared-confirmation-modal
        [heading]="'macrodeck.app:Migration.Offer.Heading' | translate"
        [message]="'macrodeck.app:Migration.Offer.Message' | translate"
        [confirmText]="'macrodeck.app:Migration.Offer.Confirm' | translate"
        [cancelText]="'macrodeck.app:Migration.Offer.Dismiss' | translate"
        (confirm)="accept()"
        (cancel)="offer.dismiss()">
      </shared-confirmation-modal>
    }
  `,
})
export class MigrationOfferModalComponent {
  protected readonly offer = inject(MigrationOfferService);
  private readonly wizard = inject(MigrationWizardService);
  private readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly migration = inject(MigrationService);
  private readonly onboarding = inject(OnboardingService);

  private readonly isConnected = computed(() => this.api.connectionStateSignal() === 'connected');
  private presented = false;
  private detectedChoice: MigrationSourceChoice | null = null;

  constructor() {
    effect(() => {
      if (this.presented || !this.offer.pending()) {
        return;
      }
      // Not `!== 'pending'`: while the onboarding state is still unknown the wizard may yet be owed,
      // and this offer must never appear in front of it (issue #893).
      if (this.onboarding.state() !== 'done') {
        return;
      }
      if (this.isConnected() && this.auth.state() === 'authenticated') {
        this.presented = true;
        void this.checkAndPresent();
      }
    });
  }

  protected accept(): void {
    const choice = this.detectedChoice;
    this.offer.dismiss();
    if (choice) {
      this.wizard.open(choice);
    }
  }

  private async checkAndPresent(): Promise<void> {
    const outcome = await this.migration.getSources();
    if (outcome.status !== 'success') {
      // A transient failure right after connecting is not "nothing was found" - leave the flag armed
      // so the next authenticated load (a fresh instance of this component) tries again.
      return;
    }

    const detected = outcome.sources.find(source => !!source.defaultPath);
    if (!detected) {
      this.offer.dismiss();
      return;
    }

    this.detectedChoice = {
      sourceId: detected.id,
      sourceName: detected.name,
      input: { kind: 'path', path: detected.defaultPath ?? '' },
    };
    this.offer.present();
  }
}
