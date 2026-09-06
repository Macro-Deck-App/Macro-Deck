import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { AppStrings, MigrationSummary, MigrationWarningDto, resolveLocalizedText } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';

export interface MigrationResult {
  summary: MigrationSummary;
  profileIds: string[];
}

@Component({
  selector: 'app-migration-result-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './migration-result-modal.component.html',
  styleUrls: ['./migration-result-modal.component.scss'],
})
export class MigrationResultModalComponent {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true })
  set result(value: MigrationResult) {
    this.data.set(value);
  }

  @Output() done = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly data = signal<MigrationResult | null>(null);

  protected readonly profileCountLabel = computed(() => {
    const count = this.data()?.profileIds.length ?? 0;
    return this.localization.translateKey(AppStrings.Migration.Result.ProfileCreatedCount, { count });
  });

  protected readonly warnings = computed(() => this.data()?.summary.warnings ?? []);

  protected warningDetail(warning: MigrationWarningDto): string {
    return resolveLocalizedText(warning.detail, this.localization);
  }

  protected onDone(): void {
    dismissModal(this.modal, () => this.done.emit());
  }
}
