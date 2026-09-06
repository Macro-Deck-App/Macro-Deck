import { FormsModule } from '@angular/forms';

import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnDestroy,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';

import { ActionBlock, ActionBlockParameter, ActionFlow, AppStrings, ConditionExpression, EventDefinition, EventTriggerBinding, ParameterValue, createEmptyComparison, defaultEventConfigurationValue, eventConfigurationValues, qualifiedEventId } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import { refineEventConfigurationParameters } from '../../../domain/event-parameter-refinement.util';
import { isParameterVisible } from '../../../domain/parameter-visibility.util';
import { EventCatalogService } from '../../../services/event-catalog.service';
import { ConditionBuilderComponent, LeafStateLookup } from '../../condition-builder/condition-builder.component';
import { ConditionEvalService } from '../services/condition-eval.service';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';
import { ParamRowComponent } from '../action-card/param-list/param-row.component';
import { ActionFlowStore } from '../services/action-flow.store';

@Component({
  selector: 'shared-event-trigger-editor',
  standalone: true,
  imports: [FormsModule, SelectComponent, ParamRowComponent, ConditionBuilderComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './event-trigger-editor.component.html',
  styleUrls: ['./event-trigger-editor.component.scss'],
})
export class EventTriggerEditorComponent implements OnDestroy {
  private readonly catalog = inject(EventCatalogService);
  private readonly conditionEval = inject(ConditionEvalService, { optional: true });
  protected readonly store = inject(ActionFlowStore);
  private readonly localization = inject(LocalizationService);

  protected readonly removeTriggerAriaLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.EventTrigger.RemoveTriggerAriaLabel));
  protected readonly removeTriggerLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.EventTrigger.RemoveTrigger));
  protected readonly providerFieldLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.EventTrigger.ProviderField));
  protected readonly eventFieldLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.EventTrigger.EventField));
  protected readonly providerPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.EventTrigger.ProviderPlaceholder));
  protected readonly eventPlaceholder = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.EventTrigger.EventPlaceholder));
  protected readonly extraConditionLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.EventTrigger.ExtraCondition));
  protected readonly removeConditionLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.EventTrigger.RemoveCondition));
  protected readonly addConditionLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.EventTrigger.AddCondition));
  protected readonly pickEventHint = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.EventTrigger.PickEventHint));
  protected readonly sectionLabel = computed(() => this.localization.translateKey(
    this.isScheduled() ? AppStrings.ActionBuilder.EventTrigger.Schedule : AppStrings.ActionBuilder.EventTrigger.OnlyRunWhen,
  ));

  private registeredKey: string | null = null;

  private readonly flowSignal = signal<ActionFlow | undefined>(undefined);

  @Input({ required: true })
  set flow(value: ActionFlow) {
    this.flowSignal.set(value);
  }

  @Input() removable = true;

  protected readonly loading = this.catalog.loading;
  protected readonly loadError = this.catalog.error;

  protected readonly providerOptions = computed<SelectOption[]>(() =>
    this.catalog.groups().map(group => ({ value: group.providerId, label: group.providerName })),
  );

  protected readonly selectedProviderId = computed(() => this.flowSignal()?.event?.providerId ?? '');

  protected readonly eventOptions = computed<SelectOption[]>(() => {
    const providerId = this.selectedProviderId();
    if (!providerId) return [];
    const group = this.catalog.groups().find(g => g.providerId === providerId);
    return (group?.events ?? []).map(event => ({
      value: event.id,
      label: event.category ? `${event.category} - ${event.name}` : event.name,
    }));
  });

  protected readonly selectedEventId = computed(() => qualifiedEventId(this.flowSignal()?.event) ?? '');

  protected readonly definition = computed<EventDefinition | undefined>(() =>
    this.catalog.find(this.selectedEventId()),
  );

  protected readonly parameters = computed<ActionBlockParameter[]>(() => {
    const definition = this.definition();
    if (!definition) return [];
    const stored = this.flowSignal()?.event?.parameters ?? [];

    const merged = definition.configurationParameters.map(declared => {
      const existing = stored.find(p => p.name === declared.name);
      return {
        ...declared,
        value: existing ? existing.value : defaultEventConfigurationValue(definition, declared),
        valueLabel: existing?.valueLabel,
        operator: existing?.operator,
      } as ActionBlockParameter;
    });

    return refineEventConfigurationParameters(definition, merged, this.store.variables());
  });

  protected readonly visibleParameters = computed<ActionBlockParameter[]>(() => {
    const all = this.parameters();
    return all.filter(param => isParameterVisible(param, all));
  });

  protected readonly configurationValues = computed<Record<string, unknown>>(() =>
    eventConfigurationValues(this.parameters()));

  protected readonly parameterBlock = computed<ActionBlock>(() => ({
    id: this.triggerId,
    type: 'trigger',
    blockType: 'event',
    label: '',
    color: '',
    integrationId: this.selectedProviderId(),
    actionId: '',
    parameters: this.parameters(),
  }));

  protected readonly isScheduled = computed(() => this.definition()?.deliveryKind === 'scheduled');

  protected readonly filter = computed(() => this.flowSignal()?.event?.filter);

  constructor() {
    void this.catalog.load();

    // Owns its filter's evaluation slot the same way a condition section does, but keyed by the
    // event as well: the host resolves $event against that event's last real occurrence, so two
    // triggers holding the same expression must not share one verdict.
    effect(() => {
      const flow = this.flowSignal();
      const expression = this.filter();
      if (!this.conditionEval) return;

      const key = flow ? `event:${flow.triggerId}` : null;
      if (this.registeredKey && this.registeredKey !== key) {
        this.conditionEval.unregister(this.registeredKey);
        this.registeredKey = null;
      }

      if (!key || !expression) {
        if (key && this.registeredKey === key) {
          this.conditionEval.unregister(key);
          this.registeredKey = null;
        }
        return;
      }

      this.conditionEval.register(key, expression, this.selectedEventId() || undefined);
      this.registeredKey = key;
    });
  }

  ngOnDestroy(): void {
    if (this.conditionEval && this.registeredKey) {
      this.conditionEval.unregister(this.registeredKey);
    }
  }

  protected get leafState(): LeafStateLookup {
    const key = this.registeredKey;
    return (leafId: string) => (key ? this.conditionEval?.leafState(key, leafId) : undefined);
  }

  protected onProviderChange(providerId: string | number | null): void {
    const flow = this.flowSignal();
    if (!flow) return;
    this.store.updateEventBinding(flow.triggerId, {
      providerId: `${providerId ?? ''}`,
      eventId: '',
    });
  }

  protected onEventChange(qualifiedId: string | number | null): void {
    const flow = this.flowSignal();
    if (!flow) return;

    const definition = this.catalog.find(`${qualifiedId ?? ''}`);
    if (!definition) return;

    const binding: EventTriggerBinding = {
      providerId: definition.providerId,
      eventId: definition.id.slice(definition.providerId.length + 2),
      eventName: definition.name,
      parameters: definition.configurationParameters.map(declared => ({
        ...declared,
        value: defaultEventConfigurationValue(definition, declared),
      }) as ActionBlockParameter),
    };

    this.store.updateEventBinding(flow.triggerId, binding);
  }

  protected onFilterChange(expression: ConditionExpression): void {
    const flow = this.flowSignal();
    if (flow) this.store.updateEventFilter(flow.triggerId, expression);
  }

  protected addFilter(): void {
    const flow = this.flowSignal();
    if (flow) this.store.updateEventFilter(flow.triggerId, createEmptyComparison());
  }

  protected removeFilter(): void {
    const flow = this.flowSignal();
    if (flow) this.store.updateEventFilter(flow.triggerId, undefined);
  }

  protected remove(): void {
    const flow = this.flowSignal();
    if (flow) this.store.removeEventTrigger(flow.triggerId);
  }

  protected get triggerId(): string {
    return this.flowSignal()?.triggerId ?? '';
  }

  protected trackParam = (_: number, param: ActionBlockParameter): string => param.name;

  protected readonly noValue: ParameterValue = '';
}
