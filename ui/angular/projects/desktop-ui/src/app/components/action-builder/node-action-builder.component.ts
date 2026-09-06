import { ChangeDetectionStrategy, Component, computed, forwardRef, inject, input, output } from '@angular/core';

import type { ActionFlow, UiNodeOption } from '@macro-deck/runtime';
import { VariableService } from '@shared';
import { UiRenderContext } from '../ui-render/ui-render-context';
import { ActionService } from '../../services/action.service';
import { IntegrationService } from '../../services/integration.service';
import { ActionBuilderComponent } from './action-builder.component';
import { TOGGLE_TRIGGER_TYPE } from './default-action-defs';

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
      [allowRun]="canRun()"
      [unsavedChanges]="unsavedChanges()"
      (flowsChange)="flowsChange.emit($event)" />
  `,
})
export class NodeActionBuilderComponent {
  private readonly actionService = inject(ActionService);
  private readonly integrationService = inject(IntegrationService);
  private readonly variableService = inject(VariableService);
  private readonly context = inject(UiRenderContext);

  readonly flows = input<ActionFlow[]>([]);
  readonly triggers = input<string[] | undefined>(undefined);
  readonly states = input<UiNodeOption[] | undefined>(undefined);
  readonly canRun = input(false);

  readonly flowsChange = output<ActionFlow[]>();

  protected readonly availableBlocks = this.actionService.actionBlockDefinitions;
  protected readonly scopeRefId = computed(() => this.context.scopeRefId);
  protected readonly unsavedChanges = computed(() => this.context.unsavedChanges);

  protected readonly showToggleTriggers = computed(() => !!this.triggers()?.includes(TOGGLE_TRIGGER_TYPE));

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
