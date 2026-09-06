import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { AppStrings, MigrationSummary, MigrationWarningDto, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';

interface ContentEntry {
  icon: string;
  label: string;
}

const COUNT_KEY = {
  profile: AppStrings.Migration.Preview.ProfileCount,
  folder: AppStrings.Migration.Preview.FolderCount,
  widget: AppStrings.Migration.Preview.WidgetCount,
  icon: AppStrings.Migration.Preview.IconCount,
  variable: AppStrings.Migration.Preview.VariableCount,
  migratedAction: AppStrings.Migration.Preview.MigratedActionCount,
};

@Component({
  selector: 'app-migration-preview-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, ErrorBannerComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './migration-preview-modal.component.html',
  styleUrls: ['./migration-preview-modal.component.scss'],
})
export class MigrationPreviewModalComponent {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true })
  set summary(value: MigrationSummary) {
    this.data.set(value);
  }

  @Input() bannerMessage: string | null = null;
  @Input() submitting = false;

  @Output() confirmed = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<void>();
  @Output() bannerDismissed = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly data = signal<MigrationSummary | null>(null);

  protected readonly heading = computed(() =>
    this.localization.translateKey(AppStrings.Migration.Preview.Heading, { source: this.data()?.sourceName ?? '' }));

  protected readonly contents = computed<ContentEntry[]>(() => {
    const summary = this.data();
    if (!summary) {
      return [];
    }

    const entries: ContentEntry[] = [
      { icon: 'grid', label: this.plural(summary.profileCount, COUNT_KEY.profile) },
      { icon: 'folder', label: this.plural(summary.folderCount, COUNT_KEY.folder) },
      { icon: 'action-button-type', label: this.plural(summary.widgetCount, COUNT_KEY.widget) },
    ];
    if (summary.iconCount > 0) {
      entries.push({ icon: 'image', label: this.plural(summary.iconCount, COUNT_KEY.icon) });
    }
    if (summary.variableCount > 0) {
      entries.push({ icon: 'braces-x', label: this.plural(summary.variableCount, COUNT_KEY.variable) });
    }
    if (summary.migratedActionCount > 0) {
      entries.push({ icon: 'zap', label: this.plural(summary.migratedActionCount, COUNT_KEY.migratedAction) });
    }
    return entries;
  });

  protected readonly bestEffortNote = computed(() =>
    this.localization.translateKey(AppStrings.Migration.Preview.BestEffortMessage, {
      source: this.data()?.sourceName ?? '',
    }));

  protected readonly credentialNote = computed(() => {
    switch (this.data()?.credentialStatus) {
      case 'Skipped':
        return this.localization.translateKey(AppStrings.Migration.Preview.CredentialStatusSkipped);
      case 'Decrypted':
        return this.localization.translateKey(AppStrings.Migration.Preview.CredentialStatusDecrypted);
      default:
        return null;
    }
  });

  protected readonly unsupportedActionsSummary = computed(() => {
    const count = this.data()?.unsupportedActionCount ?? 0;
    return count > 0 ? this.plural(count, AppStrings.Migration.Preview.UnsupportedActionCount) : null;
  });

  protected warningDetail(warning: MigrationWarningDto): string {
    return resolveLocalizedText(warning.detail, this.localization);
  }

  protected occurrenceLabel(count: number): string {
    return this.plural(count, AppStrings.Migration.Preview.OccurrenceCount);
  }

  protected onConfirm(): void {
    this.confirmed.emit();
  }

  protected onCancel(): void {
    dismissModal(this.modal, () => this.cancelled.emit());
  }

  private plural(count: number, key: string): string {
    return this.localization.translateKey(key, { count });
  }
}
