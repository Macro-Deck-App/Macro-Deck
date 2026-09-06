import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import { IconImportBatchModel, IconPackService, isTerminalBatchState } from '../../services/icon-pack.service';

const AUTO_DISMISS_DELAY_MS = 4000;

@Component({
  selector: 'shared-import-progress',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './import-progress.component.html',
  styleUrls: ['./import-progress.component.scss']
})
export class ImportProgressComponent {
  private readonly iconPacks = inject(IconPackService);
  private readonly localization = inject(LocalizationService);
  private readonly dismissTimers = new Map<string, ReturnType<typeof setTimeout>>();

  protected readonly batches = computed(() => Array.from(this.iconPacks.activeBatches().values()));

  protected readonly sourceNameFallback = computed(
    () => this.localization.translateKey(AppStrings.Store.IconImport.SourceNameFallback));

  protected readonly dismissLabel = computed(
    () => this.localization.translateKey(AppStrings.Store.IconImport.Dismiss));

  constructor() {
    effect(() => {
      for (const batch of this.batches()) {
        if (batch.state === 'Completed' && batch.failed === 0 && !this.dismissTimers.has(batch.id)) {
          this.dismissTimers.set(batch.id, setTimeout(() => {
            this.dismissTimers.delete(batch.id);
            this.iconPacks.dismissBatch(batch.id);
          }, AUTO_DISMISS_DELAY_MS));
        }
      }
    });
  }

  protected isTerminal(batch: IconImportBatchModel): boolean {
    return isTerminalBatchState(batch.state);
  }

  protected progressPercent(batch: IconImportBatchModel): number | null {
    if (!batch.total || batch.total <= 0) {
      return null;
    }

    return Math.min(100, Math.round(((batch.processed + batch.failed) / batch.total) * 100));
  }

  protected statusText(batch: IconImportBatchModel): string {
    switch (batch.state) {
      case 'Discovering':
        return this.localization.translateKey(AppStrings.Store.IconImport.Discovering);
      case 'Processing':
        return this.localization.translateKey(AppStrings.Store.IconImport.ProcessingProgress, {
          processed: batch.processed + batch.failed,
          total: batch.total ?? '?',
        });
      case 'Completed':
        return this.localization.translateKey(AppStrings.Store.IconImport.ImportedCount, { count: batch.processed });
      case 'CompletedWithErrors':
        return this.localization.translateKey(AppStrings.Store.IconImport.ImportedWithFailures, {
          processed: batch.processed,
          failed: batch.failed,
        });
      case 'Failed':
        return batch.error ?? this.localization.translateKey(AppStrings.Store.IconImport.ImportFailed);
      case 'Cancelled':
        return this.localization.translateKey(AppStrings.Store.IconImport.CancelledKeptCount, { count: batch.processed });
    }
  }

  protected dismiss(batch: IconImportBatchModel): void {
    const timer = this.dismissTimers.get(batch.id);
    if (timer) {
      clearTimeout(timer);
      this.dismissTimers.delete(batch.id);
    }

    this.iconPacks.dismissBatch(batch.id);
  }

  protected cancel(batch: IconImportBatchModel): void {
    void this.iconPacks.cancelBatch(batch.id);
  }
}
