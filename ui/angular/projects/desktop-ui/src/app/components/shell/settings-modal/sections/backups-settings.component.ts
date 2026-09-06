import { untracked, effect, computed, ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AppStrings, BackupComponentCatalogEntry, BackupComponentGroup, BackupComponentGroupInfo, BackupSummary } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, SettingsSectionComponent, ToastService, TranslatePipe } from '@shared';
import { BackupPreviewModalComponent } from '../../../backup/backup-preview-modal/backup-preview-modal.component';
import { BackupProgressCardComponent } from '../../../backup/backup-progress-card/backup-progress-card.component';
import { BackupRestoreModalComponent } from '../../../backup/backup-restore-modal/backup-restore-modal.component';
import { RecoveryKeyPromptModalComponent } from '../../../backup/recovery-key-prompt-modal/recovery-key-prompt-modal.component';
import { ConfirmationModalComponent } from '../../../overlay/confirmation-modal/confirmation-modal.component';
import { BackupService } from '../../../../services/backup.service';
import { BackupListComponent } from './backup-list.component';
import { BackupRecoveryKeySettingsComponent } from './backup-recovery-key-settings.component';
import { BackupScheduleSettingsComponent } from './backup-schedule-settings.component';

type BackupsModalState =
  | { kind: 'none' }
  | { kind: 'preview'; backup: BackupSummary }
  | { kind: 'deleteConfirm'; backup: BackupSummary }
  | { kind: 'recoveryKeyPrompt'; backup: BackupSummary; invalid: boolean }
  | {
      kind: 'restorePick';
      backup: BackupSummary;
      catalog: BackupComponentCatalogEntry[];
      components: BackupComponentGroupInfo[];
      recoveryKey?: string;
    }
  | { kind: 'restoreConfirm'; backup: BackupSummary; components: BackupComponentGroup[]; recoveryKey?: string };

@Component({
  selector: 'app-backups-settings',
  standalone: true,
  imports: [
    SettingsSectionComponent,
    ButtonComponent,
    ConfirmationModalComponent,
    BackupPreviewModalComponent,
    BackupProgressCardComponent,
    ModalComponent,
    BackupRestoreModalComponent,
    RecoveryKeyPromptModalComponent,
    BackupListComponent,
    BackupScheduleSettingsComponent,
    BackupRecoveryKeySettingsComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './backups-settings.component.html',
  styleUrls: ['./backups-settings.component.scss'],
})
export class BackupsSettingsComponent {
  private readonly backupService = inject(BackupService);
  private readonly toastService = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  readonly canMutate = this.backupService.canMutate;
  readonly creating = signal(false);
  readonly modal = signal<BackupsModalState>({ kind: 'none' });

  protected readonly operation = this.backupService.operation;

  protected readonly operationFailed = computed(() => this.operation()?.stage === 'Failed');

  protected dismissOperation(): void {
    this.backupService.dismissOperation();
  }

  private readonly clearFinishedOperation = effect(() => {
    const op = this.operation();
    if (op?.stage !== 'Completed') {
      return;
    }

    untracked(() => {
      this.toastService.show(this.localization.translateKey(AppStrings.Settings.Backups.CompletedToast), { variant: 'success' });
      this.backupService.dismissOperation();
    });
  });

  constructor() {
    void this.backupService.load();
  }

  async createBackup(): Promise<void> {
    this.creating.set(true);
    try {
      const result = await this.backupService.createBackup();
      if (!result.ok) {
        this.toastService.show(result.error, { variant: 'error' });
      }
    } finally {
      this.creating.set(false);
    }
  }

  onPreview(backup: BackupSummary): void {
    this.modal.set({ kind: 'preview', backup });
  }

  onDeleteRequested(backup: BackupSummary): void {
    this.modal.set({ kind: 'deleteConfirm', backup });
  }

  async confirmDelete(): Promise<void> {
    const state = this.modal();
    if (state.kind !== 'deleteConfirm') {
      return;
    }
    this.modal.set({ kind: 'none' });
    const result = await this.backupService.deleteBackup(state.backup.id);
    if (!result.ok) {
      this.toastService.show(result.error, { variant: 'error' });
    }
  }

  async onRestoreRequested(backup: BackupSummary): Promise<void> {
    await this.inspectForRestore(backup);
  }

  onRecoveryKeySubmitted(key: string): void {
    const state = this.modal();
    if (state.kind !== 'recoveryKeyPrompt') {
      return;
    }
    void this.inspectForRestore(state.backup, key);
  }

  onRestorePicked(components: BackupComponentGroup[]): void {
    const state = this.modal();
    if (state.kind !== 'restorePick') {
      return;
    }
    this.modal.set({ kind: 'restoreConfirm', backup: state.backup, components, recoveryKey: state.recoveryKey });
  }

  async confirmRestore(): Promise<void> {
    const state = this.modal();
    if (state.kind !== 'restoreConfirm') {
      return;
    }
    this.modal.set({ kind: 'none' });
    const outcome = await this.backupService.prepareRestore(state.backup.id, state.components, state.recoveryKey);
    if (outcome.status === 'error') {
      this.toastService.show(outcome.message, { variant: 'error' });
    } else if (outcome.status !== 'staged') {
      this.toastService.show(this.localization.translateKey(AppStrings.Settings.Backups.RecoveryKeyRequiredToast), { variant: 'error' });
    }
  }

  closeModal(): void {
    this.modal.set({ kind: 'none' });
  }

  private async inspectForRestore(backup: BackupSummary, recoveryKey?: string): Promise<void> {
    const outcome = await this.backupService.inspectBackup(backup.id, recoveryKey);
    if (outcome.status === 'success') {
      this.modal.set({
        kind: 'restorePick',
        backup,
        catalog: outcome.catalog,
        components: outcome.components,
        recoveryKey,
      });
      return;
    }
    if (outcome.status === 'recoveryKeyRequired') {
      this.modal.set({ kind: 'recoveryKeyPrompt', backup, invalid: false });
      return;
    }
    if (outcome.status === 'recoveryKeyInvalid') {
      this.modal.set({ kind: 'recoveryKeyPrompt', backup, invalid: true });
      return;
    }
    this.modal.set({ kind: 'none' });
    this.toastService.show(outcome.message, { variant: 'error' });
  }
}
