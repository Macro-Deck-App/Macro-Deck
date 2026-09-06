import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { AppStrings, StoreOperationBody } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import { StoreOperationService } from '../../services/store-operation.service';
import { formatEta, isTerminalStoreOperationState, storeOperationByteReadout, storeOperationPercent, storeOperationStateLabelKey } from '../../util/store-operation-display';

const AUTO_DISMISS_DELAY_MS = 4000;

@Component({
  selector: 'shared-store-progress',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shared-store-progress.component.html',
  styleUrls: ['./shared-store-progress.component.scss'],
})
export class SharedStoreProgressComponent {
  private readonly operations = inject(StoreOperationService);
  private readonly localization = inject(LocalizationService);
  private readonly dismissTimers = new Map<string, ReturnType<typeof setTimeout>>();

  protected readonly visible = computed(() =>
    this.operations.all().filter(op => op.state !== 'Cancelled'));

  constructor() {
    effect(() => {
      for (const operation of this.visible()) {
        if (operation.state === 'Completed' && !this.dismissTimers.has(operation.id)) {
          this.dismissTimers.set(operation.id, setTimeout(() => {
            this.dismissTimers.delete(operation.id);
            void this.operations.dismiss(operation.id);
          }, AUTO_DISMISS_DELAY_MS));
        }
      }
    });
  }

  protected isTerminal(operation: StoreOperationBody): boolean {
    return isTerminalStoreOperationState(operation.state);
  }

  protected label(operation: StoreOperationBody): string {
    const key = storeOperationStateLabelKey(operation.state);
    return key ? this.localization.translateKey(key) : operation.state;
  }

  protected percent(operation: StoreOperationBody): number | null {
    return storeOperationPercent(operation);
  }

  protected readout(operation: StoreOperationBody): string {
    return storeOperationByteReadout(operation, this.localization);
  }

  protected eta(operation: StoreOperationBody): string | null {
    return formatEta(operation.etaSeconds, this.localization);
  }

  protected errorMessage(operation: StoreOperationBody): string {
    return operation.errorMessage || operation.error || this.localization.translateKey(AppStrings.Store.OperationFailed);
  }

  protected dismiss(operation: StoreOperationBody): void {
    const timer = this.dismissTimers.get(operation.id);
    if (timer) {
      clearTimeout(timer);
      this.dismissTimers.delete(operation.id);
    }

    void this.operations.dismiss(operation.id);
  }

  protected retry(operation: StoreOperationBody): void {
    void this.operations.retry(operation.id);
  }
}
