import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActionBlock, ActionBlockParameter, AppStrings, EventDefinition, ParameterValue, defaultParameterValue } from '@macro-deck/runtime';
import { ButtonComponent, InputComponent, LocalizationService, LocalizedTextPipe, TranslatePipe } from '@shared';
import { ParamRowComponent } from '../../../action-builder/action-card/param-list/param-row.component';
import { ActionFlowStore } from '../../../action-builder/services/action-flow.store';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../../feedback/loading-state/loading-state.component';
import { RailItemComponent } from '../../../rail-page/rail-item.component';
import { RailPageComponent } from '../../../rail-page/rail-page.component';
import { isParameterVisible } from '../../../../domain/parameter-visibility.util';
import { EventCatalogService } from '../../../../services/event-catalog.service';

interface TriggerResult {
  success: boolean;
  queuedSubscriptions: number;
  error?: { code: string; message: string };
}

@Component({
  selector: 'app-events-tab',
  standalone: true,
  imports: [
    FormsModule,
    ButtonComponent,
    EmptyStateComponent,
    InputComponent,
    LoadingStateComponent,
    LocalizedTextPipe,
    ParamRowComponent,
    RailItemComponent,
    RailPageComponent,
    TranslatePipe,
  ],
  providers: [ActionFlowStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './events-tab.component.html',
  styleUrls: ['./events-tab.component.scss'],
})
export class EventsTabComponent {
  protected readonly catalog = inject(EventCatalogService);
  private readonly localization = inject(LocalizationService);

  protected readonly selectedProviderId = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly selectedEventId = signal<string | null>(null);
  protected readonly values = signal<Record<string, ParameterValue>>({});
  protected readonly triggering = signal(false);
  protected readonly result = signal<TriggerResult | null>(null);

  protected readonly providers = computed(() => this.catalog.groups());

  protected readonly heading = computed(() => {
    const providerId = this.selectedProviderId();
    if (providerId === null) {
      return this.localization.translateKey(AppStrings.Developer.Events.AllEventsLabel);
    }
    return this.providers().find(p => p.providerId === providerId)?.providerName ?? providerId;
  });

  protected readonly filteredEvents = computed<EventDefinition[]>(() => {
    const providerId = this.selectedProviderId();
    const query = this.search().trim().toLowerCase();
    return this.catalog.events()
      .filter(e => providerId === null || e.providerId === providerId)
      .filter(e => {
        if (query === '') return true;
        const name = e.name.toLowerCase();
        const category = (e.category ?? '').toLowerCase();
        return name.includes(query) || e.id.toLowerCase().includes(query) || category.includes(query);
      })
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  protected readonly selectedEvent = computed<EventDefinition | null>(() => {
    const id = this.selectedEventId();
    if (!id) {
      return null;
    }
    return this.filteredEvents().find(e => e.id === id) ?? null;
  });

  protected readonly isScheduled = computed(() => this.selectedEvent()?.deliveryKind === 'scheduled');

  protected readonly paramRows = computed<ActionBlockParameter[]>(() => {
    const values = this.values();
    return (this.selectedEvent()?.payloadParameters ?? []).map(def => ({
      ...def,
      value: values[def.name] ?? defaultParameterValue(def),
    }));
  });

  protected readonly visibleParamRows = computed<ActionBlockParameter[]>(() => {
    const rows = this.paramRows();
    return rows.filter(row => isParameterVisible(row, rows));
  });

  protected readonly triggerBlock = computed<ActionBlock>(() => {
    const event = this.selectedEvent();
    return {
      id: 'event-trigger',
      type: 'action',
      blockType: event?.id ?? 'event-trigger',
      label: event?.name ?? '',
      color: 'var(--color-accent)',
      parameters: this.paramRows(),
    };
  });

  constructor() {
    void this.catalog.reload();
  }

  protected selectProvider(providerId: string | null): void {
    this.selectedProviderId.set(providerId);
  }

  protected select(event: EventDefinition): void {
    this.selectedEventId.set(event.id);
    this.result.set(null);

    const values: Record<string, ParameterValue> = {};
    for (const def of event.payloadParameters) {
      values[def.name] = defaultParameterValue(def);
    }
    this.values.set(values);
  }

  protected setValue(name: string, value: ParameterValue): void {
    this.values.update(current => ({ ...current, [name]: value }));
  }

  protected async trigger(): Promise<void> {
    const event = this.selectedEvent();
    if (!event || this.triggering() || this.isScheduled()) {
      return;
    }

    this.triggering.set(true);
    this.result.set(null);
    try {
      const response = await this.catalog.trigger(event.id, this.values());
      this.result.set({
        success: response.success,
        queuedSubscriptions: response.queuedSubscriptions ?? 0,
        error: response.error,
      });
    } catch {
      this.result.set({
        success: false,
        queuedSubscriptions: 0,
        error: {
          code: 'NETWORK_ERROR',
          message: this.localization.translateKey(AppStrings.Developer.Events.NetworkErrorMessage),
        },
      });
    } finally {
      this.triggering.set(false);
    }
  }
}
