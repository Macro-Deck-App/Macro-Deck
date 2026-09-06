import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { AppStrings, StoreCatalogItemBody, StoreOperationBody } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, TranslatePipe } from '@shared';
import { DeveloperModeService } from '../../services/developer-mode.service';
import { formatEta, storeOperationByteReadout, storeOperationErrorKey, storeOperationPercent } from '../../util/store-operation-display';

type EffectiveState =
  | 'install' | 'installed' | 'update' | 'unsupported'
  | 'queued' | 'downloading' | 'validating' | 'installing' | 'completed' | 'failed';

@Component({
  selector: 'shared-store-install-button',
  standalone: true,
  imports: [ButtonComponent, ModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-install-button.component.html',
  styleUrls: ['./store-install-button.component.scss'],
})
export class StoreInstallButtonComponent {
  readonly item = input.required<StoreCatalogItemBody>();
  readonly operation = input<StoreOperationBody | null>(null);

  readonly size = input<'compact' | 'lg'>('compact');

  protected readonly prominent = computed(() => this.size() === 'lg');

  private readonly localization = inject(LocalizationService);

  protected readonly notSupportedOnPlatform = computed(
    () => this.localization.translateKey(AppStrings.Store.NotSupportedOnPlatform));

  readonly install = output<void>();
  readonly retry = output<void>();

  readonly uninstall = output<void>();

  readonly installUnsigned = output<void>();

  private readonly developerMode = inject(DeveloperModeService);

  constructor() {
    // Only asked once the host has already refused this install for being unsigned: nothing is
    // pre-flighted, and a store that never hits that refusal never reads the flag at all.
    effect(() => {
      if (this.isUnsignedRefusal()) {
        void this.developerMode.ensureLoaded();
      }
    });
  }

  protected readonly effective = computed<EffectiveState>(() => {
    const op = this.operation();
    if (op) {
      switch (op.state) {
        case 'Queued': return 'queued';
        case 'Downloading': return 'downloading';
        case 'Validating': return 'validating';
        case 'Installing': return 'installing';
        case 'Failed': return 'failed';
        case 'Completed': return 'completed';
        case 'Cancelled': break;
      }
    }

    switch (this.item().installState) {
      case 'NotInstalled': return 'install';
      case 'Installed': return 'installed';
      case 'UpdateAvailable': return 'update';
      case 'Unsupported': return 'unsupported';
    }
  });

  protected readonly percent = computed(() => {
    const op = this.operation();
    return op ? storeOperationPercent(op) : null;
  });

  protected readonly readout = computed(() => {
    const op = this.operation();
    return op ? storeOperationByteReadout(op, this.localization) : '';
  });

  protected readonly errorMessage = computed(() =>
    this.localization.translateKey(storeOperationErrorKey(this.operation()?.error)));

  protected readonly eta = computed(() => {
    const op = this.operation();
    return op ? formatEta(op.etaSeconds, this.localization) : null;
  });

  protected readonly errorDetail = computed(() => this.operation()?.errorMessage ?? null);

  protected readonly isUnsignedRefusal = computed(() => {
    const op = this.operation();
    return op?.state === 'Failed' && op.error === 'UnsignedNotPermitted' && this.item().kind === 'Plugin';
  });

  protected readonly canInstallUnsigned = computed(
    () => this.isUnsignedRefusal() && this.developerMode.enabled());

  protected readonly unsignedWarningOpen = signal(false);

  protected readonly detailsOpen = signal(false);

  protected openDetails(): void {
    this.detailsOpen.set(true);
  }

  protected closeDetails(): void {
    this.detailsOpen.set(false);
  }

  protected onRetryFromDetails(): void {
    this.detailsOpen.set(false);
    this.retry.emit();
  }

  protected openUnsignedWarning(): void {
    this.detailsOpen.set(false);
    this.unsignedWarningOpen.set(true);
  }

  protected closeUnsignedWarning(): void {
    this.unsignedWarningOpen.set(false);
  }

  protected onConfirmUnsigned(): void {
    this.unsignedWarningOpen.set(false);
    this.installUnsigned.emit();
  }

  protected onInstall(): void {
    this.install.emit();
  }

  protected onRetry(): void {
    this.retry.emit();
  }

  protected onUninstall(): void {
    this.uninstall.emit();
  }
}
