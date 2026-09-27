import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { AppStrings, StoreCatalogItemBody, StoreOperationBody } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, TranslatePipe } from '@shared';
import { DeveloperModeService } from '../../services/developer-mode.service';
import { TooltipDirective } from '../overlay/tooltip/tooltip.directive';
import { compareVersions, sameVersion } from '../../util/semver-compare';
import { formatEta, storeOperationByteReadout, storeOperationErrorKey, storeOperationPercent } from '../../util/store-operation-display';

type EffectiveState =
  | 'install' | 'installVersion' | 'installed' | 'update' | 'downgrade' | 'testBuild' | 'unsupported' | 'unavailable'
  | 'queued' | 'downloading' | 'validating' | 'backingUp' | 'installing' | 'completed' | 'failed' | 'blocked';

type BlockedAction = 'checkForUpdates' | 'chooseVersion' | 'viewDetails' | null;

export type StoreManageAction = 'settings' | 'library';

@Component({
  selector: 'shared-store-install-button',
  standalone: true,
  imports: [ButtonComponent, ModalComponent, NgTemplateOutlet, TooltipDirective, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-install-button.component.html',
  styleUrls: ['./store-install-button.component.scss'],
  host: { '[class.prominent]': 'prominent()' },
})
export class StoreInstallButtonComponent {
  readonly item = input.required<StoreCatalogItemBody>();
  readonly operation = input<StoreOperationBody | null>(null);

  readonly size = input<'compact' | 'lg'>('compact');

  readonly targetVersion = input<string | null>(null);
  readonly targetInstallable = input(true);
  readonly targetUnavailableReason = input<string | null>(null);
  readonly otherVersionsAvailable = input(false);
  readonly updatesAvailable = input(false);
  readonly manageAction = input<StoreManageAction | null>(null);

  protected readonly prominent = computed(() => this.size() === 'lg');
  protected readonly secondarySize = computed(() => this.prominent() ? 'md' : this.size());
  protected readonly uninstallVariant = computed(() => this.prominent() ? 'danger-ghost' : 'ghost');

  private readonly localization = inject(LocalizationService);

  protected readonly notSupportedOnPlatform = computed(
    () => this.localization.translateKey(AppStrings.Store.NotSupportedOnPlatform));

  readonly install = output<void>();
  readonly retry = output<void>();

  readonly uninstall = output<void>();

  readonly installUnsigned = output<string | undefined>();
  readonly checkForUpdates = output<void>();
  readonly chooseVersion = output<void>();
  readonly viewDetails = output<void>();
  readonly manage = output<StoreManageAction>();

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

  protected readonly target = computed(() => this.targetVersion() ?? this.item().latestVersion);

  private readonly targetsLatest = computed(() => sameVersion(this.target(), this.item().latestVersion));

  private readonly interactiveFailure = computed(() => {
    const op = this.operation();
    return !!op && (op.canRetry || this.canInstallUnsigned());
  });

  protected readonly effective = computed<EffectiveState>(() => {
    const op = this.operation();
    const item = this.item();
    const testBuild = !!item.installedTestBuild
      && (item.installState === 'Installed' || item.installState === 'UpdateAvailable');
    const returnedToStore = op?.kind !== 'TestInstall' && op?.version === item.latestVersion;
    const live = !!op && op.state !== 'Completed' && op.state !== 'Failed' && op.state !== 'Cancelled';
    if (item.withdrawal && this.isInstalled() && !live) {
      return 'installed';
    }

    if (op && !(testBuild && op.state === 'Completed' && !returnedToStore)) {
      switch (op.state) {
        case 'Queued': return 'queued';
        case 'Downloading': return 'downloading';
        case 'Validating': return 'validating';
        case 'BackingUp': return 'backingUp';
        case 'Installing': return 'installing';
        case 'Failed':
          // A failed test build is the Tests tab's to report, and a failure for another version than
          // the one this button offers says nothing about installing that one.
          if (op.kind !== 'TestInstall' && sameVersion(op.version, this.target())) {
            return this.interactiveFailure() ? 'failed' : 'blocked';
          }
          break;
        case 'Completed':
          if (sameVersion(op.version, this.target())) {
            return 'completed';
          }
          break;
        case 'Cancelled': break;
      }
    }

    if (testBuild && this.targetsLatest()) {
      return 'testBuild';
    }

    if (item.installState === 'Unsupported') {
      return 'unsupported';
    }

    if (!this.targetInstallable()) {
      return 'unavailable';
    }

    // A downgrade must be confirmed first, and only the detail page, which passes a target version, asks.
    const leavesWithdrawnVersion = !!this.targetVersion() && !!item.installedVersionWithdrawal;
    if (this.targetsLatest()) {
      switch (item.installState) {
        case 'NotInstalled': return 'install';
        case 'UpdateAvailable': return 'update';
        default:
          if (!leavesWithdrawnVersion) {
            return 'installed';
          }
      }
    }

    const installed = item.installedVersion;
    if (!installed || item.installState === 'NotInstalled') {
      return 'installVersion';
    }

    if (sameVersion(installed, this.target())) {
      return 'installed';
    }

    const order = compareVersions(this.target(), installed);
    if (order === null) {
      return 'installVersion';
    }
    return order > 0 ? 'update' : 'downgrade';
  });

  protected readonly isInstalled = computed(() =>
    this.item().installState === 'Installed' || this.item().installState === 'UpdateAvailable');

  protected readonly blockedAction = computed<BlockedAction>(() => {
    switch (this.operation()?.error) {
      case 'RequiresNewerMacroDeck':
        return this.updatesAvailable() ? 'checkForUpdates' : null;
      case 'Incompatible':
      case 'VersionNotFound':
        if (this.prominent()) {
          return this.otherVersionsAvailable() ? 'chooseVersion' : null;
        }
        return 'viewDetails';
      default:
        return null;
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
    // Consent covers the refused package: a pinned refusal is re-requested as that version, an
    // unpinned one as whatever is latest now, exactly as a plain install would.
    const op = this.operation();
    this.installUnsigned.emit(op?.versionPinned ? op.version : undefined);
  }

  protected onBlockedAction(action: BlockedAction): void {
    switch (action) {
      case 'checkForUpdates': this.checkForUpdates.emit(); break;
      case 'chooseVersion': this.chooseVersion.emit(); break;
      case 'viewDetails': this.viewDetails.emit(); break;
    }
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
