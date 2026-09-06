import { ChangeDetectionStrategy, Component, Input, OnChanges, forwardRef, inject, signal } from '@angular/core';

import { SCRIPT_CALL_SITES, UiConfigEvents, WIDGET_INTEGRATION_ID, WIDGET_TARGET_SELF, isScriptInputParam, isWidgetAppearanceActionId } from '@macro-deck/runtime';
import type { ActionBlock, ActionBlockParameter, ParameterValue, UiNode, UiNodeEvent } from '@macro-deck/runtime';
import type { UiSessionHandle } from '@shared';
import { ActionOptionsService } from '../../../../services/action-options.service';
import { isParameterVisible } from '../../../../domain/parameter-visibility.util';
import { UiTreeComponent } from '../../../ui-render/ui-tree.component';
import { ActionFlowStore } from '../../services/action-flow.store';
import { ParamRowComponent } from './param-row.component';
import { RunScriptInputFieldsComponent } from './run-script-input-fields.component';
import { SetVariableActionFieldsComponent } from './set-variable-action-fields.component';
import { WidgetAppearanceActionFieldsComponent } from './widget-appearance-action-fields.component';

const VARIABLES_INTEGRATION_ID = 'app.macro-deck.variables';
const SET_VARIABLE_ACTION_ID = 'set-variable';

@Component({
  selector: 'shared-param-list',
  standalone: true,
  imports: [
    ParamRowComponent,
    RunScriptInputFieldsComponent,
    SetVariableActionFieldsComponent,
    WidgetAppearanceActionFieldsComponent,
    // `shared-ui-input` (issue #837) now renders `shared-action-builder` for its own
    // `actions-list-editor`/`action-picker` node types, which closes a real module cycle back here
    // through `shared-ui-tree` - deferred the same way `UiNodeComponent` defers its own mutual
    // references to `UiChromeComponent`/`UiInputComponent`, rather than dereferenced at this file's
    // own load time.
    forwardRef(() => UiTreeComponent),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!root && block.parameters && block.parameters.length > 0) {
      <div class="action-params">
        @for (param of block.parameters; track param.name) {
          @if (!isAppearanceParameter(param.name) && !isHiddenStateParameter(param.name)
            && !isTypedVariableParameter(param.name) && !isScriptInputParameter(param.name)
            && isVisible(param)) {
            <shared-param-row [blockId]="block.id" [block]="block" [param]="param" />
          }
        }
        @if (isWidgetAppearanceAction) { <shared-widget-appearance-action-fields [block]="block" /> }
        @if (isSetVariableAction) { <shared-set-variable-action-fields [block]="block" /> }
        @if (isRunScriptAction) { <shared-run-script-input-fields [block]="block" /> }
      </div>
    }
    @if (root) {
      <div class="action-params">
        <shared-ui-tree [root]="root" (nodeEvent)="onNodeEvent($event)" />
      </div>
    }
  `,
  styles: `
    .action-params {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      padding: 0 0.5rem 0.5rem 1.5rem;
    }
  `,
})
export class ParamListComponent implements OnChanges {
  @Input({ required: true }) block!: ActionBlock;

  @Input() root?: UiNode | null;

  @Input() session?: UiSessionHandle | null;

  private readonly options = inject(ActionOptionsService);
  private readonly store = inject(ActionFlowStore);

  private readonly singleStateTarget = signal<boolean | null>(null);
  private readonly targetIsOwner = signal(false);
  private stateRequest = 0;
  private resolvedTarget: string | null = null;

  ngOnChanges(): void {
    if (this.root) return;
    void this.resolveStateSupport();
  }

  protected onNodeEvent(event: UiNodeEvent): void {
    this.session?.send(event);
    if (event.name !== UiConfigEvents.Change) return;
    this.store.updateParam(this.block.id, event.nodeId, event.data as ParameterValue);
  }

  protected get isWidgetAction(): boolean {
    return this.block.integrationId === WIDGET_INTEGRATION_ID;
  }

  protected get isWidgetAppearanceAction(): boolean {
    return this.isWidgetAction && isWidgetAppearanceActionId(this.block.actionId);
  }

  protected get isSetVariableAction(): boolean {
    return this.block.integrationId === VARIABLES_INTEGRATION_ID &&
      this.block.actionId === SET_VARIABLE_ACTION_ID;
  }

  protected get isRunScriptAction(): boolean {
    const actionId = SCRIPT_CALL_SITES[this.block.integrationId ?? ''];
    return actionId !== undefined && actionId === this.block.actionId;
  }

  protected isScriptInputParameter(name: string): boolean {
    return this.isRunScriptAction && isScriptInputParam(name);
  }

  protected isTypedVariableParameter(name: string): boolean {
    return this.isSetVariableAction && ['operation', 'value'].includes(name);
  }

  protected isVisible(param: ActionBlockParameter): boolean {
    return isParameterVisible(param, this.block.parameters);
  }

  protected isAppearanceParameter(name: string): boolean {
    return this.isWidgetAppearanceAction && !['widget', 'state'].includes(name);
  }

  // Only an appearance action's state selector is hideable: "the target has one appearance, so there is
  // nothing to choose" says nothing about Set Button State, whose state IS the value it writes.
  protected isHiddenStateParameter(name: string): boolean {
    if (name !== 'state' || !this.isWidgetAppearanceAction) return false;

    if (this.targetIsOwner()) {
      const draft = this.store.previewScopeHasOnOffStates();
      if (draft !== undefined) return !draft;
    }

    return this.singleStateTarget() === true;
  }

  private async resolveStateSupport(): Promise<void> {
    if (!this.isWidgetAppearanceAction) {
      this.singleStateTarget.set(null);
      this.resolvedTarget = null;
      return;
    }

    const target = this.targetValue();
    this.targetIsOwner.set(target !== '' && target === this.store.previewScopeRefId());
    if (target === this.resolvedTarget) return;
    this.resolvedTarget = target;

    const request = ++this.stateRequest;
    try {
      const response = await this.options.getOptions({
        integrationId: WIDGET_INTEGRATION_ID,
        actionId: this.block.actionId ?? '',
        parameterName: 'state',
        currentParameters: { widget: target },
      });
      if (request !== this.stateRequest) return;
      this.singleStateTarget.set(response.options.length === 1);
    } catch {
      // A failed lookup must not hide a selector the target may well support - and must not be
      // remembered as answered either.
      if (request !== this.stateRequest) return;
      this.singleStateTarget.set(null);
      this.resolvedTarget = null;
    }
  }

  private targetValue(): string {
    const target = this.block.parameters?.find(parameter => parameter.name === 'widget')?.value;
    const value = typeof target === 'string' ? target : '';
    if (value !== WIDGET_TARGET_SELF && value !== '') return value;
    return this.store.previewScopeRefId() ?? '';
  }
}
