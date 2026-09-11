import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  EventEmitter,
  Injector,
  afterNextRender,
  Input,
  OnChanges,
  Output,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';

import { ActionBlockDefinition, ActionFlow, AppStrings, eventConfigurationValues, eventPayloadVariables, hydrateBlockParameters, isEventFlow, isSameTriggerType, normalizeEventTriggerNames, normalizeLegacyParamTypes, qualifiedEventId, resolveLocalizedText } from '@macro-deck/runtime';
import { ContextMenuComponent, ContextMenuItem, LocalizationService, OverlayPanelComponent, ToastService, TranslatePipe, UiFontService } from '@shared';
import type { Variable, VariableScope } from '@macro-deck/runtime';
import { ActionFlowValidationResult } from '../../domain/action-flow-validation.util';
import { isToolbarCompact, measureToolbarNaturalWidth } from './action-builder-layout';
import { offersPasteMenu } from './paste-target.util';
import { TEMPLATE_PREVIEW_SERVICE } from '../../domain/template-preview.interface';
import { TabBarComponent } from '../tab-bar/tab-bar.component';
import type { TabItem } from '../tab-bar/tab-bar.model';
import { NestedActionListComponent } from './action-card/nested-action-list.component';
import { ActionPickerComponent } from './action-picker/action-picker.component';
import { ActionFlowOwner } from '../../services/action-clipboard.service';
import { EventCatalogService } from '../../services/event-catalog.service';
import { EventTriggerEditorComponent } from './event-trigger/event-trigger-editor.component';
import { SelectCaretComponent } from '../forms/select-caret/select-caret.component';
import { VariableBrowserModalComponent } from '../variables/variable-browser-modal.component';
import { ConfirmationModalComponent } from '../overlay/confirmation-modal/confirmation-modal.component';
import { ActionFlowStore, IconProviderToggleRequest, StateProviderToggleRequest } from './services/action-flow.store';
import { ActionDragService } from './services/action-drag.service';
import { ConditionEvalService } from './services/condition-eval.service';
import { DEFAULT_TRIGGER_TYPE, TOGGLE_TRIGGER_TYPE, TriggerTab, defaultActionDefs, defaultTriggerTabs, toggleTriggerTab } from './default-action-defs';

// Re-exported for backwards compatibility with existing imports.
export { comparisonOperatorOptions } from './default-action-defs';

@Component({
  selector: 'shared-action-builder',
  standalone: true,
  imports: [
    TabBarComponent,
    NestedActionListComponent,
    ActionPickerComponent,
    EventTriggerEditorComponent,
    OverlayPanelComponent,
    ContextMenuComponent,
    ConfirmationModalComponent,
    SelectCaretComponent,
    VariableBrowserModalComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './action-builder.component.html',
  styleUrls: ['./action-builder.component.scss'],
  providers: [ActionFlowStore, ActionDragService, ConditionEvalService],
})
export class ActionBuilderComponent implements OnChanges, AfterViewInit {
  protected readonly store = inject(ActionFlowStore);
  protected readonly drag = inject(ActionDragService);
  private readonly conditionEval = inject(ConditionEvalService);
  private readonly templatePreview = inject(TEMPLATE_PREVIEW_SERVICE, { optional: true });
  private readonly eventCatalog = inject(EventCatalogService);
  private readonly toasts = inject(ToastService);
  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly toolbarCompact = signal(false);
  private toolbarResizeObserver: ResizeObserver | null = null;
  private toolbarMeasureScheduled = false;
  private readonly injector = inject(Injector);
  private readonly localization = inject(LocalizationService);
  private readonly uiFonts = inject(UiFontService);
  private readonly translate = (key: string): string => this.localization.translateKey(key);

  protected readonly builtInTriggerTabs = computed<readonly TriggerTab[]>(() => defaultTriggerTabs(this.translate));

  protected readonly addTriggerLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Toolbar.AddTrigger));
  protected readonly noEventTriggersYet = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Toolbar.NoEventTriggersYet));
  protected readonly addEventTriggerLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Toolbar.AddEventTrigger));
  protected readonly variablesTooltip = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Toolbar.VariablesOfThisWidget));
  protected readonly variablesLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Toolbar.Variables));
  protected readonly runLabel = computed(() => this.localization.translateKey(
    this.store.running() ? AppStrings.ActionBuilder.Toolbar.Running : AppStrings.ActionBuilder.Toolbar.Run,
  ));
  protected readonly loadingTriggerLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.LoadingTrigger));
  protected readonly addActionLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.AddAction));
  protected readonly pasteCopiedActionTooltip = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.PasteCopiedActionTooltip));
  protected readonly pasteLabel = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.Paste));
  protected readonly removeTriggerHeading = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.RemoveTriggerHeading));
  protected readonly widgetVariablesHeading = computed(() =>
    this.localization.translateKey(AppStrings.ActionBuilder.WidgetVariablesHeading));

  @Input()
  set flows(value: ActionFlow[]) {
    const normalized = normalizeEventTriggerNames(normalizeLegacyParamTypes(value ?? []), this.localization);
    this.store.flows.set(hydrateBlockParameters(normalized, this.lookupBlockDefinition));
    this.validityChange.emit(this.store.validation());
  }
  get flows(): ActionFlow[] {
    return this.store.flows();
  }

  @Input() set variables(value: Variable[]) {
    this.store.variables.set(value ?? []);
  }

  @Input() set runsOnWidget(value: boolean | null | undefined) {
    this.store.scriptRunsOnWidget.set(value ?? false);
  }

  @Input() set previewScope(value: VariableScope) {
    this.store.previewScope.set(value);
  }
  @Input() set previewScopeRefId(value: string | undefined) {
    this.store.previewScopeRefId.set(value);
  }

  @Input() set flowOwner(value: ActionFlowOwner | null | undefined) {
    this.store.flowOwner.set(value ?? null);
  }

  @Input() set previewScopeStates(value: { id: string; label: string }[] | undefined) {
    this.store.previewScopeStates.set(value);
  }

  @Input()
  set availableBlocks(value: ActionBlockDefinition[] | null | undefined) {
    this.availableBlocksInput.set(value ?? []);
    this.store.availableBlocks.set(value ?? []);
    // Definitions can arrive after `flows` was already bound (integrations still loading, or a
    // migrated placeholder waiting on its integration) - re-hydrate what's already in the store so
    // those blocks pick up their descriptors as soon as they can be resolved.
    this.store.flows.update(flows => hydrateBlockParameters(flows, this.lookupBlockDefinition));
  }

  @Input()
  set stateProviderBlockId(value: string | undefined) {
    this.store.stateProviderBlockId.set(value);
  }

  @Input()
  set supportsStateProvider(value: boolean) {
    this.store.consumerSupportsStateProvider.set(value);
  }

  @Input()
  set iconProviderBlockId(value: string | undefined) {
    this.store.iconProviderBlockId.set(value);
  }

  @Input()
  set supportsIconProvider(value: boolean) {
    this.store.consumerSupportsIconProvider.set(value);
  }

  @Input()
  set showToggleTriggers(value: boolean) {
    this.showToggleTriggersInput.set(value);
  }

  @Input()
  set triggerTabs(value: TriggerTab[] | null | undefined) {
    this.triggerTabsInput.set(value?.length ? [...value] : null);
  }

  @Input()
  set allowEventTriggers(value: boolean) {
    this.allowEventTriggersInput.set(value);
  }

  @Input()
  set singleEventTrigger(value: boolean) {
    this.singleEventTriggerInput.set(value);
  }

  @Input()
  set allowRun(value: boolean) {
    this.allowRunInput.set(value);
  }

  // A run executes on the host against the *saved* widget, so running against a draft would act on
  // something other than what the editor shows (issue #312).
  @Input() set unsavedChanges(value: boolean) {
    this.unsavedChangesInput.set(value);
  }

  @Input()
  set auxTabId(value: string | undefined) {
    this.auxTabIdInput.set(value);
  }
  @Input()
  set auxTabLabel(value: string) {
    this.auxTabLabelInput.set(value);
  }
  @Input()
  set auxTabDot(value: boolean) {
    this.auxTabDotInput.set(value);
  }
  @Input()
  set auxTabBadge(value: number | undefined) {
    this.auxTabBadgeInput.set(value);
  }
  @Input()
  set auxTabInline(value: boolean) {
    this.auxTabInlineInput.set(value);
  }

  @Output() flowsChange = new EventEmitter<ActionFlow[]>();

  @Output() validityChange = new EventEmitter<ActionFlowValidationResult>();

  @Output() runCompleted = new EventEmitter<void>();

  @Output() stateProviderChange = new EventEmitter<StateProviderToggleRequest>();

  @Output() iconProviderChange = new EventEmitter<IconProviderToggleRequest>();

  private readonly availableBlocksInput = signal<ActionBlockDefinition[]>([]);

  private readonly lookupBlockDefinition = (integrationId: string, actionId: string): ActionBlockDefinition | undefined =>
    this.availableBlocksInput().find(d => d.integrationId === integrationId && d.actionId === actionId);

  private readonly showToggleTriggersInput = signal(false);
  private readonly triggerTabsInput = signal<TriggerTab[] | null>(null);
  private readonly allowEventTriggersInput = signal(true);
  private readonly singleEventTriggerInput = signal(false);
  private readonly allowRunInput = signal(true);
  private readonly unsavedChangesInput = signal(false);
  private readonly auxTabIdInput = signal<string | undefined>(undefined);
  private readonly auxTabLabelInput = signal('');
  private readonly auxTabDotInput = signal(false);
  private readonly auxTabBadgeInput = signal<number | undefined>(undefined);
  private readonly auxTabInlineInput = signal(false);

  readonly effectiveTriggerTabs = computed<TriggerTab[]>(() => {
    if (this.singleEventTriggerInput()) {
      return [];
    }

    const override = this.triggerTabsInput();
    if (override) {
      return override;
    }

    return this.showToggleTriggersInput()
      ? [...this.builtInTriggerTabs(), toggleTriggerTab(this.translate)]
      : [...this.builtInTriggerTabs()];
  });

  readonly showTabRow = computed(() =>
    this.effectiveTriggerTabs().length > 1 || this.showAddEventTrigger() || !!this.auxTabItem(),
  );

  readonly showAddEventTrigger = computed(() =>
    !this.singleEventTriggerInput() && this.allowEventTriggersInput(),
  );

  readonly eventTriggerRemovable = computed(() => !this.singleEventTriggerInput());

  readonly awaitingTrigger = computed(() =>
    this.singleEventTriggerInput() && this.store.eventFlows().length === 0,
  );

  readonly showRun = computed(() => this.allowRunInput() && !this.isAuxSelected());

  readonly showVariables = computed(() =>
    this.store.previewScope() === 'widget' && !!this.store.previewScopeRefId());

  readonly variablesOpen = signal(false);

  protected readonly scopedVariables = computed(() =>
    this.store.previewScopeRefId() ? this.store.variables() : []);

  protected openVariables(): void {
    this.variablesOpen.set(true);
  }

  readonly canRun = computed(() => this.store.canRun() && !this.unsavedChangesInput());

  readonly runTitle = computed(() => {
    const T = AppStrings.ActionBuilder.Toolbar;
    if (this.store.running()) return this.localization.translateKey(T.RunningTooltip);
    if (this.unsavedChangesInput()) return this.localization.translateKey(T.SaveFirstTooltip);
    if (!this.store.canRun()) return this.localization.translateKey(T.AddActionToRunTooltip);
    return this.localization.translateKey(T.RunNowTooltip);
  });

  readonly isAuxSelected = computed<boolean>(() => {
    const auxId = this.auxTabIdInput();
    return !!auxId && this.store.selectedTriggerId() === auxId;
  });

  readonly selectedEventFlow = computed(() => this.store.selectedEventFlow());

  private readonly configuredTriggerTypes = computed(() => new Set(
    this.store.flows()
      .filter(f => !isEventFlow(f))
      .map(f => f.triggerType.toLowerCase()),
  ));

  private readonly hasInteractionTrigger = computed(() => {
    const configured = this.configuredTriggerTypes();
    return this.builtInTriggerTabs().some(t => configured.has(t.triggerType.toLowerCase()));
  });

  readonly visibleTriggerTabs = computed<TriggerTab[]>(() => {
    if (this.triggerTabsInput() || this.singleEventTriggerInput()) {
      return this.effectiveTriggerTabs();
    }

    const configured = this.configuredTriggerTypes();
    const fallback = this.hasInteractionTrigger() ? '' : DEFAULT_TRIGGER_TYPE;
    return this.effectiveTriggerTabs().filter(
      t => t.triggerType === TOGGLE_TRIGGER_TYPE
        || t.triggerType === fallback
        || configured.has(t.triggerType.toLowerCase()),
    );
  });

  readonly addableTriggerTabs = computed<TriggerTab[]>(() => {
    if (this.triggerTabsInput() || this.singleEventTriggerInput()) return [];
    const visible = new Set(this.visibleTriggerTabs().map(t => t.triggerType));
    return this.effectiveTriggerTabs().filter(t => !visible.has(t.triggerType));
  });

  readonly showAddTrigger = computed(() => this.addableTriggerTabs().length > 0);

  private isRemovableTrigger(triggerType: string): boolean {
    if (this.triggerTabsInput() || this.singleEventTriggerInput()) return false;
    if (triggerType === TOGGLE_TRIGGER_TYPE) return false;
    return this.configuredTriggerTypes().has(triggerType.toLowerCase());
  }

  readonly triggerTabItems = computed<TabItem[]>(() =>
    this.visibleTriggerTabs().map(t => {
      const flow = this.store.flows().find(
        f => !isEventFlow(f) && isSameTriggerType(f.triggerType, t.triggerType),
      );
      return {
        id: t.triggerType,
        label: t.label,
        badge: flow && flow.children.length > 0 ? flow.children.length : undefined,
        removable: this.isRemovableTrigger(t.triggerType),
      };
    }),
  );

  readonly eventTabItems = computed<TabItem[]>(() =>
    this.store.eventFlows().map((flow, index) => ({
      id: flow.triggerId,
      label: flow.event?.eventName?.trim()
        || this.localization.translateKey(AppStrings.ActionBuilder.Toolbar.EventFallbackLabel, { index: index + 1 }),
      badge: flow.children.length > 0 ? flow.children.length : undefined,
    })),
  );

  readonly eventMenuLabel = computed(() => {
    const eventsLabel = this.localization.translateKey(AppStrings.ActionBuilder.Toolbar.Events);
    const selected = this.selectedEventFlow();
    if (!selected) return eventsLabel;
    return this.eventTabItems().find(item => item.id === selected.triggerId)?.label ?? eventsLabel;
  });

  readonly eventMenuOpen = signal(false);

  readonly auxTabItem = computed<TabItem | undefined>(() => {
    const auxId = this.auxTabIdInput();
    return auxId
      ? {
        id: auxId,
        label: this.auxTabLabelInput(),
        dot: this.auxTabDotInput(),
        badge: this.auxTabBadgeInput(),
      }
      : undefined;
  });

  readonly isAuxTabInline = computed(() => this.auxTabInlineInput() && !!this.auxTabItem());

  readonly auxTabItems = computed<TabItem[]>(() => {
    const aux = this.auxTabItem();
    return aux ? [aux] : [];
  });

  readonly mainTabItems = computed<TabItem[]>(() => {
    const aux = this.auxTabItem();
    return this.isAuxTabInline() && aux ? [...this.triggerTabItems(), aux] : this.triggerTabItems();
  });

  readonly availableActionDefs = computed(() =>
    [...defaultActionDefs(this.translate), ...this.availableBlocksInput()],
  );

  readonly currentChildren = computed(() => {
    const selected = this.store.selectedTriggerId();
    const flow = this.store.flows().find(f => f.triggerId === selected)
      ?? this.store.flows().find(f => !isEventFlow(f) && isSameTriggerType(f.triggerType, selected));
    return flow?.children ?? [];
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => this.toolbarResizeObserver?.disconnect());

    const stateProviderSub = this.store.stateProviderRequests$.subscribe(
      request => this.stateProviderChange.emit(request),
    );
    destroyRef.onDestroy(() => stateProviderSub.unsubscribe());

    const iconProviderSub = this.store.iconProviderRequests$.subscribe(
      request => this.iconProviderChange.emit(request),
    );
    destroyRef.onDestroy(() => iconProviderSub.unsubscribe());

    effect(() => {
      this.triggerTabItems();
      this.eventTabItems();
      this.auxTabItem();
      this.showRun();
      this.showAddTrigger();
      this.uiFonts.version();
      this.scheduleToolbarMeasure();
    });

    this.store.connect(
      flows => {
        this.flowsChange.emit(flows);
        this.validityChange.emit(this.store.validation());
      },
      triggerType => this.effectiveTriggerTabs().find(t => t.triggerType === triggerType)?.label,
    );

    void this.eventCatalog.load();

    effect(() => {
      const flow = this.selectedEventFlow();
      const definition = this.eventCatalog.find(qualifiedEventId(flow?.event));
      this.store.eventVariables.set(eventPayloadVariables(definition));
      this.store.eventDefinition.set(definition);
      this.store.eventConfigurationValues.set(eventConfigurationValues(flow?.event?.parameters));
    });

    effect(() => {
      const items = this.triggerTabItems();
      const aux = this.auxTabItem();
      const selected = this.store.selectedTriggerId();
      const eventItems = this.eventTabItems();
      const isValidSelection =
        items.some(item => item.id === selected) ||
        eventItems.some(item => item.id === selected) ||
        aux?.id === selected;
      if (isValidSelection) {
        return;
      }

      const fallback = items[0]?.id ?? eventItems[0]?.id;
      if (fallback) {
        this.store.selectedTriggerId.set(fallback);
      }
    });
  }

  ngAfterViewInit(): void {
    this.drag.attach(this.host.nativeElement);

    this.toolbarResizeObserver = new ResizeObserver(() => this.measureToolbar());
    this.toolbarResizeObserver.observe(this.host.nativeElement);
    this.measureToolbar();
  }

  measureToolbar(): void {
    const root = this.host.nativeElement as HTMLElement;
    const row = root.querySelector<HTMLElement>('.tab-row');
    if (!row) return;

    const wasCompact = this.toolbarCompact();
    const compact = isToolbarCompact(measureToolbarNaturalWidth(row), row.clientWidth, wasCompact);
    if (compact === wasCompact) return;
    this.toolbarCompact.set(compact);
  }

  private scheduleToolbarMeasure(): void {
    if (this.toolbarMeasureScheduled) return;
    this.toolbarMeasureScheduled = true;
    afterNextRender(
      () => {
        this.toolbarMeasureScheduled = false;
        this.measureToolbar();
      },
      { injector: this.injector },
    );
  }

  ngOnChanges(): void {
    this.conditionEval.configure(
      this.templatePreview ?? undefined,
      this.store.previewScope(),
      this.store.previewScopeRefId(),
    );
    this.validityChange.emit(this.store.validation());
  }

  selectTab(triggerId: string): void {
    this.store.selectedTriggerId.set(triggerId);
    this.store.closePicker();
    this.eventMenuOpen.set(false);
  }

  toggleEventMenu(): void {
    this.eventMenuOpen.update(open => !open);
  }

  addEventTrigger(): void {
    this.store.addEventTrigger();
    this.store.closePicker();
    this.eventMenuOpen.set(false);
  }

  readonly addTriggerMenuOpen = signal(false);

  toggleAddTriggerMenu(): void {
    this.addTriggerMenuOpen.update(open => !open);
  }

  addTrigger(triggerType: string): void {
    this.addTriggerMenuOpen.set(false);
    // The stand-in Short Press tab owns no flow, so adding a second trigger would make it vanish.
    // Give it one first: adding a trigger must not take one away (#480).
    if (!this.hasInteractionTrigger() && triggerType !== DEFAULT_TRIGGER_TYPE) {
      this.store.addTrigger(DEFAULT_TRIGGER_TYPE);
    }
    this.store.addTrigger(triggerType);
    this.store.closePicker();
    this.eventMenuOpen.set(false);
  }

  readonly pendingTriggerRemoval = signal<TriggerTab | null>(null);

  readonly pendingTriggerRemovalMessage = computed(() => {
    const tab = this.pendingTriggerRemoval();
    if (!tab) return '';

    const count = this.store.flows().find(
      f => !isEventFlow(f) && isSameTriggerType(f.triggerType, tab.triggerType),
    )?.children.length ?? 0;
    return count > 0
      ? this.localization.translateKey(
          AppStrings.ActionBuilder.RemoveTriggerWithActions, { label: tab.label, count },
        )
      : this.localization.translateKey(AppStrings.ActionBuilder.RemoveTriggerNoActions, { label: tab.label });
  });

  onTriggerTabRemove(tabId: string): void {
    if (!this.isRemovableTrigger(tabId)) return;

    const tab = this.visibleTriggerTabs().find(t => t.triggerType === tabId);
    if (tab) this.pendingTriggerRemoval.set(tab);
  }

  confirmTriggerRemoval(): void {
    const tab = this.pendingTriggerRemoval();
    this.pendingTriggerRemoval.set(null);
    if (tab) this.store.removeTrigger(tab.triggerType);
  }

  cancelTriggerRemoval(): void {
    this.pendingTriggerRemoval.set(null);
  }

  protected readonly panelMenu = signal<{ isOpen: boolean; x: number; y: number }>({
    isOpen: false,
    x: 0,
    y: 0,
  });

  protected readonly panelMenuItems = computed<ContextMenuItem[]>(() => [
    { id: 'paste', label: 'Paste', icon: 'icon-clipboard', disabled: !this.store.canPaste() },
  ]);

  protected onPanelContextMenu(event: MouseEvent): void {
    if (!offersPasteMenu(event)) return;
    event.preventDefault();
    this.panelMenu.set({ isOpen: true, x: event.clientX, y: event.clientY });
  }

  protected closePanelMenu(): void {
    this.panelMenu.update(menu => ({ ...menu, isOpen: false }));
  }

  protected onPanelMenuAction(action: string): void {
    if (action === 'paste') this.pasteIntoRoot();
  }

  protected pasteIntoRoot(): void {
    void this.store.pasteIntoList(this.store.rootListId());
  }

  async runFlow(): Promise<void> {
    const response = await this.store.runSelectedFlow();
    if (!response) return;

    switch (response.status) {
      case 'Succeeded':
        this.toasts.show(this.localization.translateKey(AppStrings.ActionBuilder.Store.RunSucceeded, {
          duration: formatRunDuration(response.durationMs ?? 0),
        }));
        this.runCompleted.emit();
        break;
      case 'PartiallyFailed': {
        const actions = response.actions ?? [];
        const failed = actions.filter(action => action.status === 'Failed');
        this.toasts.show(this.localization.translateKey(AppStrings.ActionBuilder.Store.RunPartiallyFailed, {
          count: failed.length,
          total: actions.length,
        }), {
          variant: 'error',
          detail: resolveLocalizedText(failed[0]?.errorMessage, this.localization),
        });
        break;
      }
      case 'Accepted':
        break;
      case 'Failed':
      case 'Cancelled':
      default:
        // `||`, not `??` - resolveLocalizedText returns '' (not null/undefined) for unresolvable
        // input, and an empty toast is worse than the generic fallback.
        this.toasts.show(
          resolveLocalizedText(response.error?.message, this.localization)
            || this.localization.translateKey(AppStrings.Errors.Folder.ActionRunFailed),
          { variant: 'error' },
        );
        break;
    }
  }
}

function formatRunDuration(durationMs: number): string {
  return durationMs >= 1000 ? `${(durationMs / 1000).toFixed(1)}s` : `${durationMs}ms`;
}
