import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, LocalizationService, ModalComponent, TranslatePipe } from '@shared';
import { MigrationSession } from '../../../domain/migration-session';
import { MigrationService } from '../../../services/migration.service';
import { MigrationWizardService } from '../../../services/migration-wizard.service';
import { RecoveryKeyPromptModalComponent } from '../../backup/recovery-key-prompt-modal/recovery-key-prompt-modal.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { MigrationPreviewModalComponent } from '../migration-preview-modal/migration-preview-modal.component';
import { MigrationResultModalComponent } from '../migration-result-modal/migration-result-modal.component';

@Component({
  selector: 'app-migration-wizard',
  standalone: true,
  imports: [
    ModalComponent,
    ButtonComponent,
    ErrorBannerComponent,
    ConfirmationModalComponent,
    RecoveryKeyPromptModalComponent,
    MigrationPreviewModalComponent,
    MigrationResultModalComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './migration-wizard.component.html',
  styleUrls: ['./migration-wizard.component.scss'],
})
export class MigrationWizardComponent {
  private readonly migration = inject(MigrationService);
  private readonly localization = inject(LocalizationService);
  protected readonly wizard = inject(MigrationWizardService);

  protected readonly session = new MigrationSession(this.migration);

  private readonly sourceName: string;

  protected readonly keyPromptDescription = computed(() =>
    this.localization.translateKey(AppStrings.Migration.KeyPrompt.Description, {
      source: this.session.credentialSummary()?.sourceName ?? this.sourceName,
    }));

  protected readonly credentialChoiceMessage = computed(() =>
    this.localization.translateKey(AppStrings.Migration.CredentialChoice.Message, {
      source: this.session.credentialSummary()?.sourceName ?? this.sourceName,
    }));

  constructor() {
    const choice = this.wizard.takeChoice();
    this.sourceName = choice?.sourceName ?? '';
    if (choice) {
      void this.session.preview(choice.sourceId, choice.input);
    }
  }

  protected close(): void {
    this.wizard.close();
  }
}
