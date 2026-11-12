import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import {
  AppStrings,
  StoreRegistryRefreshLogEntryBody,
  StoreRegistryRefreshRunBody,
  StoreRegistryRefreshRunState,
  StoreRegistryRefreshStep,
  Strings,
} from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent } from '@shared';

type RefreshStepState = 'done' | 'current' | 'failed';

interface RefreshLogLine {
  key: string;
  time: string;
  text: string;
  detail: string | null;
  state: RefreshStepState;
}

const STATE_KEYS: Record<StoreRegistryRefreshRunState, string> = {
  Running: AppStrings.Store.RegistryRefresh.State.Running,
  Succeeded: AppStrings.Store.RegistryRefresh.State.Succeeded,
  Failed: AppStrings.Store.RegistryRefresh.State.Failed,
  Cancelled: AppStrings.Store.RegistryRefresh.State.Cancelled,
};

const STEP_KEYS: Record<StoreRegistryRefreshStep, string> = {
  Started: AppStrings.Store.RegistryRefresh.Step.Started,
  FetchingManifest: AppStrings.Store.RegistryRefresh.Step.FetchingManifest,
  FetchingSignature: AppStrings.Store.RegistryRefresh.Step.FetchingSignature,
  UpToDate: AppStrings.Store.RegistryRefresh.Step.UpToDate,
  DownloadingFiles: AppStrings.Store.RegistryRefresh.Step.DownloadingFiles,
  Verifying: AppStrings.Store.RegistryRefresh.Step.Verifying,
  ReadingCatalog: AppStrings.Store.RegistryRefresh.Step.ReadingCatalog,
  Applied: AppStrings.Store.RegistryRefresh.Step.Applied,
  Failed: AppStrings.Store.RegistryRefresh.Step.Failed,
  Cancelled: AppStrings.Store.RegistryRefresh.Step.Cancelled,
  WaitingForRegistryUpdate: AppStrings.Store.RegistryRefresh.Step.WaitingForRegistryUpdate,
};

const ERROR_KEYS: Record<string, string> = {
  disabled: AppStrings.Store.RegistryRefresh.Error.Disabled,
  network_failure: AppStrings.Store.RegistryRefresh.Error.NetworkFailure,
  malformed: AppStrings.Store.RegistryRefresh.Error.Malformed,
  budget_exceeded: AppStrings.Store.RegistryRefresh.Error.BudgetExceeded,
  size_mismatch: AppStrings.Store.RegistryRefresh.Error.SizeMismatch,
  sequence_rollback: AppStrings.Store.RegistryRefresh.Error.SequenceRollback,
  signature_invalid: AppStrings.Store.RegistryRefresh.Error.SignatureInvalid,
  certificate_untrusted: AppStrings.Store.RegistryRefresh.Error.CertificateUntrusted,
  signing_key_revoked: AppStrings.Store.RegistryRefresh.Error.SigningKeyRevoked,
  snapshot_unchanged: AppStrings.Store.RegistryRefresh.Error.SnapshotUnchanged,
  storage_failure: AppStrings.Store.RegistryRefresh.Error.StorageFailure,
};

@Component({
  selector: 'app-store-registry-refresh-modal',
  standalone: true,
  imports: [ButtonComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-registry-refresh-modal.component.html',
  styleUrls: ['./store-registry-refresh-modal.component.scss'],
})
export class StoreRegistryRefreshModalComponent {
  private readonly localization = inject(LocalizationService);

  readonly run = input<StoreRegistryRefreshRunBody | null>(null);

  readonly closed = output<void>();

  readonly forceRefresh = output<void>();

  protected readonly state = computed<StoreRegistryRefreshRunState>(() => this.run()?.state ?? 'Running');

  protected readonly heading = computed(() =>
    this.localization.translateKey(AppStrings.Store.RegistryRefresh.Heading));

  protected readonly stateText = computed(() => this.localization.translateKey(STATE_KEYS[this.state()]));

  protected readonly upToDate = computed(() => {
    const run = this.run();
    return run?.state === 'Succeeded' && run.entries.at(-1)?.step === 'UpToDate';
  });

  protected readonly forceRefreshLabel = computed(() =>
    this.localization.translateKey(AppStrings.Store.RegistryRefresh.ForceRefresh));

  protected readonly doneLabel = computed(() => this.localization.translateKey(Strings.Common.Done));

  protected readonly logLabel = computed(() =>
    this.localization.translateKey(AppStrings.Store.RegistryRefresh.LogLabel));

  protected readonly progress = computed(() => {
    const run = this.run();
    if (!run || run.state !== 'Running' || run.filesTotal <= 0) {
      return null;
    }

    return {
      percent: Math.round((run.filesCompleted / run.filesTotal) * 100),
      text: this.localization.translateKey(AppStrings.Store.RegistryRefresh.Progress,
        { completed: run.filesCompleted, total: run.filesTotal }),
    };
  });

  private readonly allLines = computed<RefreshLogLine[]>(() => {
    const run = this.run();
    if (!run) {
      return [];
    }

    const last = run.entries.length - 1;
    return run.entries.map((entry, index) => ({
      key: `${run.id}:${index}`,
      time: this.time(entry.at),
      text: this.describe(entry, run),
      detail: entry.step === 'Failed' ? entry.detail ?? null : null,
      state: this.stepState(entry, index === last && run.state === 'Running'),
    }));
  });

  protected readonly outcome = computed<RefreshLogLine | null>(() =>
    this.state() === 'Running' ? null : this.allLines().at(-1) ?? null);

  protected readonly lines = computed<RefreshLogLine[]>(() =>
    this.outcome() ? this.allLines().slice(0, -1) : this.allLines());

  protected onClose(): void {
    this.closed.emit();
  }

  protected onForceRefresh(): void {
    this.forceRefresh.emit();
  }

  private stepState(entry: StoreRegistryRefreshLogEntryBody, current: boolean): RefreshStepState {
    if (entry.step === 'Failed' || entry.step === 'Cancelled') {
      return 'failed';
    }

    return current ? 'current' : 'done';
  }

  private describe(entry: StoreRegistryRefreshLogEntryBody, run: StoreRegistryRefreshRunBody): string {
    switch (entry.step) {
      case 'Started':
        return this.localization.translateKey(run.trigger === 'Scheduled'
          ? AppStrings.Store.RegistryRefresh.Step.StartedScheduled
          : AppStrings.Store.RegistryRefresh.Step.Started);
      case 'Failed':
        return this.localization.translateKey(AppStrings.Store.RegistryRefresh.Step.Failed, {
          reason: this.localization.translateKey(
            ERROR_KEYS[entry.error ?? ''] ?? AppStrings.Store.RegistryRefresh.Error.Unknown),
        });
      default:
        return this.localization.translateKey(STEP_KEYS[entry.step],
          { count: entry.count ?? 0, sequence: entry.sequence ?? 0 });
    }
  }

  private time(iso: string): string {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      return '';
    }

    const { locale, hourCycle } = this.localization.timeLocale();
    return date.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle });
  }
}
