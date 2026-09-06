import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AppStrings, GetBackupRecoveryKeyStateResponse } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, SettingsRowComponent, SettingsSectionComponent, ToastService, TranslatePipe } from '@shared';
import { RecoveryKeyModalComponent } from '../../../backup/recovery-key-modal/recovery-key-modal.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';
import { BackupService } from '../../../../services/backup.service';

type RecoveryKeyModalState =
  | { kind: 'none' }
  | { kind: 'revealConfirm' }
  | { kind: 'keyModal'; mode: 'created' | 'revealed'; key: string };

@Component({
  selector: 'app-backup-recovery-key-settings',
  standalone: true,
  imports: [
    SettingsSectionComponent,
    SettingsRowComponent,
    ButtonComponent,
    LoadingStateComponent,
    ConfirmationModalComponent,
    RecoveryKeyModalComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './backup-recovery-key-settings.component.html',
  styleUrls: ['./backup-recovery-key-settings.component.scss'],
})
export class BackupRecoveryKeySettingsComponent {
  private readonly backupService = inject(BackupService);
  private readonly toastService = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  readonly state = this.backupService.recoveryKey;
  readonly working = signal(false);
  readonly modal = signal<RecoveryKeyModalState>({ kind: 'none' });

  statusLine(state: GetBackupRecoveryKeyStateResponse): string {
    if (!state.exportedAt) {
      return this.localization.translateKey(AppStrings.Settings.Backups.RecoveryKeyNeverShown);
    }
    return this.localization.translateKey(AppStrings.Settings.Backups.RecoveryKeyLastShown, {
      when: new Date(state.exportedAt).toLocaleString(),
    });
  }

  async createKey(): Promise<void> {
    this.working.set(true);
    try {
      const outcome = await this.backupService.createRecoveryKey();
      if (outcome.status === 'success') {
        this.modal.set({ kind: 'keyModal', mode: 'created', key: outcome.key });
      } else {
        this.toastService.show(outcome.message, { variant: 'error' });
      }
    } finally {
      this.working.set(false);
    }
  }

  requestReveal(): void {
    this.modal.set({ kind: 'revealConfirm' });
  }

  cancelReveal(): void {
    this.modal.set({ kind: 'none' });
  }

  async confirmReveal(): Promise<void> {
    this.modal.set({ kind: 'none' });
    this.working.set(true);
    try {
      const outcome = await this.backupService.revealRecoveryKey();
      if (outcome.status === 'success') {
        this.modal.set({ kind: 'keyModal', mode: 'revealed', key: outcome.key });
      } else {
        this.toastService.show(outcome.message, { variant: 'error' });
      }
    } finally {
      this.working.set(false);
    }
  }

  async onAcknowledged(): Promise<void> {
    this.modal.set({ kind: 'none' });
    const result = await this.backupService.acknowledgeRecoveryKey();
    if (!result.ok) {
      this.toastService.show(result.error, { variant: 'error' });
    }
  }

  closeKeyModal(): void {
    this.modal.set({ kind: 'none' });
  }
}
