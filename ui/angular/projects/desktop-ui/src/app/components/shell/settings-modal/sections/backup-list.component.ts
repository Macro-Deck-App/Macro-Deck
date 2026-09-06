import { computed, ChangeDetectionStrategy, Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { AppStrings, BackupSummary, BackupTrigger } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { DropdownMenuComponent } from '../../../overlay/dropdown-menu/dropdown-menu.component';
import { BackupService } from '../../../../services/backup.service';
import { formatBytes } from '../../../../util/format-bytes';
import { formatRelativeTime } from '../relative-time.util';

const TRIGGER_LABEL_KEYS: Record<BackupTrigger, string> = {
  Manual: AppStrings.Settings.Backups.TriggerManual,
  Scheduled: AppStrings.Settings.Backups.TriggerScheduled,
  BeforeHostUpdate: AppStrings.Settings.Backups.TriggerBeforeHostUpdate,
  BeforePluginUpdate: AppStrings.Settings.Backups.TriggerBeforePluginUpdate,
  BeforeRestore: AppStrings.Settings.Backups.TriggerBeforeRestore,
  Imported: AppStrings.Settings.Backups.TriggerImported,
};

@Component({
  selector: 'app-backup-list',
  standalone: true,
  imports: [
    ButtonComponent,
    DropdownMenuComponent,
    EmptyStateComponent,
    ErrorBannerComponent,
    LoadingStateComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './backup-list.component.html',
  styleUrls: ['./backup-list.component.scss'],
})
export class BackupListComponent {
  private readonly backupService = inject(BackupService);
  private readonly toastService = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  readonly backups = this.backupService.backups;

  readonly pageSize = 10;

  readonly page = signal(0);

  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.backups().length / this.pageSize)));

  readonly currentPage = computed(() => Math.min(this.page(), this.pageCount() - 1));

  readonly visibleBackups = computed(() => {
    const start = this.currentPage() * this.pageSize;
    return this.backups().slice(start, start + this.pageSize);
  });

  goToPage(page: number): void {
    this.page.set(Math.max(0, Math.min(page, this.pageCount() - 1)));
  }
  readonly isLoading = this.backupService.isLoading;
  readonly loadError = this.backupService.loadError;
  readonly operation = this.backupService.operation;
  readonly pendingRestore = this.backupService.pendingRestore;
  readonly canMutate = this.backupService.canMutate;

  readonly openMenuId = signal<string | null>(null);
  readonly restarting = signal(false);
  readonly cancellingRestore = signal(false);

  readonly formatBytes = formatBytes;

  triggerLabel(trigger: BackupTrigger): string {
    return this.localization.translateKey(TRIGGER_LABEL_KEYS[trigger]);
  }

  @Output() previewRequested = new EventEmitter<BackupSummary>();
  @Output() restoreRequested = new EventEmitter<BackupSummary>();
  @Output() deleteRequested = new EventEmitter<BackupSummary>();

  setMenuOpen(backupId: string, isOpen: boolean): void {
    this.openMenuId.set(isOpen ? backupId : null);
  }

  retry(): void {
    void this.backupService.loadBackups();
  }

  async download(backup: BackupSummary): Promise<void> {
    this.openMenuId.set(null);
    const result = await this.backupService.downloadBackup(backup.id);
    if (!result.ok) {
      this.toastService.show(result.error, { variant: 'error' });
    }
  }

  preview(backup: BackupSummary): void {
    this.openMenuId.set(null);
    this.previewRequested.emit(backup);
  }

  restore(backup: BackupSummary): void {
    if (!backup.decryptableLocally || !this.canMutate()) {
      return;
    }
    this.openMenuId.set(null);
    this.restoreRequested.emit(backup);
  }

  requestDelete(backup: BackupSummary): void {
    if (!this.canMutate()) {
      return;
    }
    this.openMenuId.set(null);
    this.deleteRequested.emit(backup);
  }

  async restartNow(): Promise<void> {
    const pending = this.pendingRestore();
    if (!pending) {
      return;
    }
    this.restarting.set(true);
    try {
      const outcome = await this.backupService.commitRestore(pending.restoreId);
      if (outcome.status === 'error') {
        this.toastService.show(outcome.message, { variant: 'error' });
      } else if (!outcome.restartSupported) {
        // Not a success: the restore is staged but nothing has been applied, and the user has to act.
        this.toastService.show(this.localization.translateKey(AppStrings.Settings.Backups.RestartFailedToast), {
          detail:
            outcome.restartUnavailableReason ??
            this.localization.translateKey(AppStrings.Settings.Backups.RestartFailedDetail),
          variant: 'error',
        });
      }
    } finally {
      this.restarting.set(false);
    }
  }

  async cancelPendingRestore(): Promise<void> {
    const pending = this.pendingRestore();
    if (!pending) {
      return;
    }
    this.cancellingRestore.set(true);
    try {
      const result = await this.backupService.cancelRestore(pending.restoreId);
      if (!result.ok) {
        this.toastService.show(result.error, { variant: 'error' });
      }
    } finally {
      this.cancellingRestore.set(false);
    }
  }

  relativeTime(iso: string): string {
    return formatRelativeTime(iso, (key, args) => this.localization.translateKey(key, args));
  }

  dateLabel(iso: string): string {
    return new Date(iso).toLocaleString();
  }
}
