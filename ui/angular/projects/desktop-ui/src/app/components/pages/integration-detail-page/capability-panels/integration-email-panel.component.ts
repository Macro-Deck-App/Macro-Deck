import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { AppStrings, IntegrationEmailInboxStatus, IpcIntegrationEmailInbox } from '@macro-deck/runtime';
import { ApiService, ErrorBannerComponent, LocalizationService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { RailItemComponent } from '../../../rail-page/rail-item.component';
import { VariableGroupHeaderComponent } from '../../../variables/variable-group-header.component';
import { VariableRowComponent } from '../../../variables/variable-row.component';
import { CapabilityDataLoader } from './capability-data-loader';

const STATUS_LABELS: Record<IntegrationEmailInboxStatus, string> = {
  Connecting: AppStrings.Integrations.Detail.Email.Status.Connecting,
  Connected: AppStrings.Integrations.Detail.Email.Status.Connected,
  Disconnected: AppStrings.Integrations.Detail.Email.Status.Disconnected,
  Error: AppStrings.Integrations.Detail.Email.Status.Error,
};

const STATUS_TONES: Record<IntegrationEmailInboxStatus, string> = {
  Connecting: 'Muted',
  Connected: 'Ready',
  Disconnected: 'Warning',
  Error: 'Unavailable',
};

@Component({
  selector: 'app-integration-email-panel',
  standalone: true,
  imports: [EmptyStateComponent, ErrorBannerComponent, LoadingStateComponent, RailItemComponent, TranslatePipe, VariableGroupHeaderComponent, VariableRowComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './integration-email-panel.component.html',
  styleUrls: ['./capability-panel.scss'],
})
export class IntegrationEmailPanelComponent {
  readonly integrationId = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  protected readonly loader = new CapabilityDataLoader('email-inbox', id => this.api.getIntegrationEmailInboxes(id));

  protected readonly inboxes = computed(() => this.loader.data()?.inboxes ?? []);

  private readonly selectedRef = signal<string | null>(null);

  protected readonly selected = computed<IpcIntegrationEmailInbox | null>(() => {
    const inboxes = this.inboxes();
    return inboxes.find(inbox => inbox.ref === this.selectedRef()) ?? inboxes[0] ?? null;
  });

  constructor() {
    effect(() => {
      const id = this.integrationId();
      untracked(() => void this.loader.load(id));
    });
  }

  protected select(inbox: IpcIntegrationEmailInbox): void {
    this.selectedRef.set(inbox.ref);
  }

  protected statusLabel(status: IntegrationEmailInboxStatus): string {
    return this.localization.translateKey(STATUS_LABELS[status] ?? AppStrings.Integrations.Detail.ValueUnknown);
  }

  protected statusTone(status: IntegrationEmailInboxStatus): string {
    return STATUS_TONES[status] ?? 'Muted';
  }

  protected syncedAt(inbox: IpcIntegrationEmailInbox): string | null {
    return inbox.syncedAt
      ? this.localization.translateKey(AppStrings.Integrations.Detail.Email.SyncedAt, { time: this.format(inbox.syncedAt) })
      : null;
  }

  protected format(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }
    const { locale, hourCycle } = this.localization.timeLocale();
    return date.toLocaleString(locale, { hourCycle, dateStyle: 'medium', timeStyle: 'short' });
  }
}
