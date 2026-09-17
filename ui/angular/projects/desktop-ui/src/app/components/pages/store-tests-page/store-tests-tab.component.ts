import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { AppStrings, StoreOperationBody, StoreTestBody, StoreTestBuildBody } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { formatRelativeTime } from '../../shell/settings-modal/relative-time.util';
import { StoreMarkdownComponent } from '../../store/store-markdown.component';
import { SettingsModalService } from '../../../services/settings-modal.service';
import { StoreOperationService } from '../../../services/store-operation.service';
import { StoreTestsService } from '../../../services/store-tests.service';
import { formatBytes } from '../../../util/format-bytes';
import {
  formatEta,
  isTerminalStoreOperationState,
  storeKindIcon,
  storeOperationByteReadout,
  storeOperationErrorKey,
  storeOperationPercent,
  storeOperationStateLabelKey,
} from '../../../util/store-operation-display';

type BuildAction = 'install' | 'installTestBuild' | 'installed' | 'progress' | 'busy';

interface BuildRow {
  build: StoreTestBuildBody;
  action: BuildAction;
  available: string;
  size: string;
  operation: StoreOperationBody | null;
  percent: number | null;
  progressLabel: string;
  failure: string | null;
}

interface TestCard {
  test: StoreTestBody;
  iconUrl: string | null;
  rows: BuildRow[];
}

interface PendingInstall {
  test: StoreTestBody;
  build: StoreTestBuildBody;
}

@Component({
  selector: 'app-store-tests-tab',
  standalone: true,
  imports: [
    ButtonComponent,
    ConfirmationModalComponent,
    EmptyStateComponent,
    LoadingStateComponent,
    StoreMarkdownComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-tests-tab.component.html',
  styleUrls: ['./store-tests-tab.component.scss'],
})
export class StoreTestsTabComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly toasts = inject(ToastService);
  private readonly settingsModal = inject(SettingsModalService);
  private readonly operations = inject(StoreOperationService);
  protected readonly service = inject(StoreTestsService);

  protected readonly pluginIcon = storeKindIcon('Plugin');

  protected readonly pendingInstall = signal<PendingInstall | null>(null);
  protected readonly expandedChangelogs = signal<ReadonlySet<string>>(new Set());
  private readonly failedIcons = signal<ReadonlySet<string>>(new Set());

  protected readonly showInitialLoading = computed(() => this.service.isLoading() && !this.service.loaded());

  protected readonly cards = computed<TestCard[]>(() => this.service.tests().map(test => this.card(test)));

  protected readonly confirmMessage = computed(() => {
    const pending = this.pendingInstall();
    if (!pending) {
      return '';
    }

    const params = {
      name: pending.test.displayName,
      version: pending.build.version,
      build: pending.build.build,
      installedVersion: pending.test.installedVersion ?? '',
    };
    return pending.test.installedVersion
      ? this.localization.translateKey(AppStrings.Store.Tests.ConfirmReplaceMessage, params)
      : this.localization.translateKey(AppStrings.Store.Tests.ConfirmMessage, params);
  });

  ngOnInit(): void {
    void this.service.load();
  }

  protected retryLoad(): void {
    void this.service.load();
  }

  protected openSignIn(): void {
    this.settingsModal.open('account');
  }

  protected onIconError(packageId: string): void {
    this.failedIcons.update(current => new Set(current).add(packageId));
  }

  protected isChangelogExpanded(buildId: string): boolean {
    return this.expandedChangelogs().has(buildId);
  }

  protected toggleChangelog(buildId: string): void {
    this.expandedChangelogs.update(current => {
      const next = new Set(current);
      if (!next.delete(buildId)) {
        next.add(buildId);
      }
      return next;
    });
  }

  protected requestInstall(test: StoreTestBody, build: StoreTestBuildBody): void {
    this.pendingInstall.set({ test, build });
  }

  protected cancelInstall(): void {
    this.pendingInstall.set(null);
  }

  protected async confirmInstall(): Promise<void> {
    const pending = this.pendingInstall();
    this.pendingInstall.set(null);
    if (!pending) {
      return;
    }

    const response = await this.service.install(pending.test.packageId, pending.build.id);
    if (response.success) {
      return;
    }

    this.toasts.show(this.localization.translateKey(AppStrings.Store.Tests.InstallFailed), {
      detail: this.localization.translateKey(installErrorKey(response.error?.code)),
      variant: 'error',
    });
    if (response.error?.code === 'not_found') {
      void this.service.load();
    }
  }

  private card(test: StoreTestBody): TestCard {
    const operation = this.operations.operationFor('Plugin', test.packageId)();
    const live = operation !== null && !isTerminalStoreOperationState(operation.state);
    return {
      test,
      iconUrl: test.hasIcon && !this.failedIcons().has(test.packageId)
        ? this.api.getStoreExtensionIconUrl('Plugin', test.packageId, test.iconSha256)
        : null,
      rows: test.builds.map(build => this.row(test, build, operation, live)),
    };
  }

  private row(test: StoreTestBody,
    build: StoreTestBuildBody,
    operation: StoreOperationBody | null,
    live: boolean): BuildRow {
    const forBuild = operation?.kind === 'TestInstall' && operation.testBuildId === build.id ? operation : null;

    let action: BuildAction;
    if (live) {
      action = forBuild ? 'progress' : 'busy';
    } else if (test.installedTestBuildId === build.id) {
      action = 'installed';
    } else {
      action = test.installedVersion ? 'installTestBuild' : 'install';
    }

    return {
      build,
      action,
      available: formatRelativeTime(build.availableAt, (key, args) => this.localization.translateKey(key, args)),
      size: formatBytes(build.sizeInBytes),
      operation: forBuild,
      percent: forBuild ? storeOperationPercent(forBuild) : null,
      progressLabel: forBuild ? this.progressLabel(forBuild) : '',
      failure: !live && forBuild?.state === 'Failed'
        ? this.localization.translateKey(storeOperationErrorKey(forBuild.error))
        : null,
    };
  }

  private progressLabel(operation: StoreOperationBody): string {
    if (operation.state === 'Downloading') {
      const readout = storeOperationByteReadout(operation, this.localization);
      const eta = formatEta(operation.etaSeconds, this.localization);
      return eta ? `${readout} · ${eta}` : readout;
    }

    const key = storeOperationStateLabelKey(operation.state);
    return key ? this.localization.translateKey(key) : '';
  }
}

function installErrorKey(code: string | null | undefined): string {
  switch (code) {
    case 'sign_in_required':
      return AppStrings.Store.Error.SignInRequired;
    case 'not_found':
      return AppStrings.Store.Error.TestBuildUnavailable;
    case 'consent_required':
      return AppStrings.Store.Error.TestConsentRequired;
    case 'account_suspended':
      return AppStrings.Store.Tests.SuspendedMessage;
    case 'rate_limited':
      return AppStrings.Store.Tests.RateLimitedMessage;
    default:
      return AppStrings.Store.Tests.UnavailableMessage;
  }
}
