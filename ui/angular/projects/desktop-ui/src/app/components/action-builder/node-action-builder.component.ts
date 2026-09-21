import { ChangeDetectionStrategy, Component, computed, forwardRef, inject, input, output } from '@angular/core';

import type { ActionFlow, UiNodeOption } from '@macro-deck/runtime';
import { LocalizationService, VariableService } from '@shared';
import { UiRenderContext } from '../ui-render/ui-render-context';
import { ActionService } from '../../services/action.service';
import { IntegrationService } from '../../services/integration.service';
import { ActionBuilderComponent } from './action-builder.component';
import { TOGGLE_TRIGGER_TYPE, fixedTriggerTabsFor } from './default-action-defs';

export interface ProviderChangeRequest {
  capability: 'state' | 'icon';
  blockId: string;
  enabled: boolean;
}

@Component({
  selector: 'shared-node-action-builder',
  standalone: true,
  imports: [
    // `ActionBuilderComponent` renders a parameter's own descriptive UI through `shared-ui-tree`
    // (`ParamListComponent`), which closes a real cycle back through `shared-ui-input` to this
    // module - deferred the same way `ui-input.component.ts` used to defer it directly.
    forwardRef(() => ActionBuilderComponent),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-action-builder
      [flows]="flows()"
      [availableBlocks]="availableBlocks()"
      [variables]="variables()"
      previewScope="widget"
      [previewScopeRefId]="scopeRefId()"
      [previewScopeStates]="previewScopeStates()"
      [showToggleTriggers]="showToggleTriggers()"
      [triggerTabs]="fixedTriggerTabs()"
      [alwaysShowTabRow]="!!fixedTriggerTabs()"
      [allowRun]="canRun()"
      [unsavedChanges]="unsavedChanges()"
      [supportsStateProvider]="offersStateProvider()"
      [supportsIconProvider]="offersIconProvider()"
      [stateProviderBlockId]="stateProviderBlockId()"
      [iconProviderBlockId]="iconProviderBlockId()"
      (flowsChange)="flowsChange.emit($event)"
      (stateProviderChange)="providerChange.emit({ capability: 'state', blockId: $event.blockId, enabled: $event.checked })"
      (iconProviderChange)="providerChange.emit({ capability: 'icon', blockId: $event.blockId, enabled: $event.checked })" />
  `,
})
export class NodeActionBuilderComponent {
  private readonly actionService = inject(ActionService);
  private readonly integrationService = inject(IntegrationService);
  private readonly variableService = inject(VariableService);
  private readonly context = inject(UiRenderContext);
  private readonly localization = inject(LocalizationService);
  private readonly translate = (key: string): string => this.localization.translateKey(key);

  readonly flows = input<ActionFlow[]>([]);
  readonly triggers = input<string[] | undefined>(undefined);
  readonly states = input<UiNodeOption[] | undefined>(undefined);
  readonly canRun = input(false);
  readonly offersStateProvider = input(false);
  readonly offersIconProvider = input(false);
  readonly stateProviderBlockId = input<string | undefined>(undefined);
  readonly iconProviderBlockId = input<string | undefined>(undefined);

  readonly flowsChange = output<ActionFlow[]>();
  readonly providerChange = output<ProviderChangeRequest>();

  protected readonly availableBlocks = this.actionService.actionBlockDefinitions;
  protected readonly scopeRefId = computed(() => this.context.scopeRefId);
  protected readonly unsavedChanges = computed(() => this.context.unsavedChanges);

  protected readonly showToggleTriggers = computed(() => !!this.triggers()?.includes(TOGGLE_TRIGGER_TYPE));

  protected readonly fixedTriggerTabs = computed(() => fixedTriggerTabsFor(this.triggers(), this.translate));

  // The states the editor is drafting right now, which the host's own options endpoint cannot see until
  // the widget is saved - a state picker inside these flows merges them in.
  protected readonly previewScopeStates = computed(() =>
    this.states()?.map(state => ({ id: state.value, label: state.label ?? state.value })));

  protected readonly variables = computed(() =>
    this.variableService.visibleForActionButtonEditor(this.scopeRefId(), this.showToggleTriggers()));

  constructor() {
    void this.integrationService.loadIntegrations();
    void this.actionService.loadActions();
  }
}
