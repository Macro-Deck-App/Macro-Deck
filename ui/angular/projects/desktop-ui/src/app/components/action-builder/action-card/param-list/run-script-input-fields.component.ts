import { ChangeDetectionStrategy, Component, Input, OnChanges, inject, signal } from '@angular/core';

import { AppStrings, RUN_SCRIPT_WIDGET_PARAM, SCRIPT_CALL_SITES, SCRIPT_RUNS_ON_WIDGET_METADATA_KEY, WIDGET_TARGET_SELF, isScriptInputParam, parseScriptInputsMetadata, scriptInputDefaultValue, scriptInputParamName, scriptInputParameter } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import type { ActionBlock, ActionBlockParameter, ScriptInput } from '@macro-deck/runtime';
import { ActionOptionsService } from '../../../../services/action-options.service';
import { ActionFlowStore } from '../../services/action-flow.store';
import { ParamRowComponent } from './param-row.component';

const REMOTE_SCRIPT_ACTION_ID = 'run-remote-script';

const SCRIPT_ID_PARAMETER = 'scriptId';

@Component({
  selector: 'shared-run-script-input-fields',
  standalone: true,
  imports: [ParamRowComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (inputParameters().length > 0 || widgetParameter() || remoteWidgetNotice()) {
      <div class="script-inputs">
        @if (inputParameters().length > 0) {
          <p class="script-inputs-heading">{{ 'macrodeck.app:ActionBuilder.RunScript.InputsHeading' | translate }}</p>
          @for (param of inputParameters(); track param.name) {
            <shared-param-row [blockId]="block.id" [block]="block" [param]="param" />
          }
        }
        @if (widgetParameter(); as param) {
          <shared-param-row [blockId]="block.id" [block]="block" [param]="param" />
        }
        @if (remoteWidgetNotice(); as notice) {
          <p class="script-inputs-notice">{{ notice }}</p>
        }
      </div>
    }
  `,
  styles: `
    :host { display: block; }
    .script-inputs { display: flex; flex-direction: column; gap: var(--space-1); }
    .script-inputs-heading {
      margin: var(--space-1) 0 0;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }
    .script-inputs-notice {
      margin: 0;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }
  `,
})
export class RunScriptInputFieldsComponent implements OnChanges {
  @Input({ required: true }) block!: ActionBlock;

  private readonly options = inject(ActionOptionsService);
  private readonly store = inject(ActionFlowStore);
  private readonly localization = inject(LocalizationService);

  protected readonly declarations = signal<ScriptInput[]>([]);

  protected readonly runsOnWidget = signal(false);

  protected readonly unresolved = signal(false);

  private request = 0;

  ngOnChanges(): void {
    void this.resolveDeclarations();
  }

  protected inputParameters(): ActionBlockParameter[] {
    if (this.unresolved()) {
      return (this.block.parameters ?? []).filter(param => isScriptInputParam(param.name));
    }
    const names = new Set(this.declarations().map(input => scriptInputParamName(input.name)));
    return (this.block.parameters ?? []).filter(param => names.has(param.name));
  }

  protected widgetParameter(): ActionBlockParameter | undefined {
    return (this.block.parameters ?? []).find(param => param.name === RUN_SCRIPT_WIDGET_PARAM);
  }

  protected remoteWidgetNotice(): string | null {
    if (this.block.actionId !== REMOTE_SCRIPT_ACTION_ID || !this.runsOnWidget()) return null;
    return this.localization.translateKey(AppStrings.ActionBuilder.RunScript.RunsOnWidgetNotRemote);
  }

  private async resolveDeclarations(): Promise<void> {
    const actionId = SCRIPT_CALL_SITES[this.block.integrationId ?? ''];
    if (!actionId || this.block.actionId !== actionId) return;

    const scriptId = this.stringParameter(SCRIPT_ID_PARAMETER);
    const instance = this.stringParameter('instance');

    const version = ++this.request;
    if (scriptId === '') {
      this.apply(version, [], false);
      return;
    }

    try {
      const response = await this.options.getOptions({
        integrationId: this.block.integrationId ?? '',
        actionId,
        parameterName: SCRIPT_ID_PARAMETER,
        currentParameters: instance === '' ? undefined : { instance },
      });
      const option = response.options.find(candidate => candidate.value === scriptId);
      if (!option) {
        // The lookup succeeded but did not contain the selected script - routine at the remote call
        // site (unreachable/ambiguous/not-configured instance, or a typed-in id that is never in the
        // list). An empty or non-matching *success* is not authoritative about what the script
        // declares, so this must not be treated as "declares nothing".
        this.markUnresolved(version);
        return;
      }
      this.apply(version, parseScriptInputsMetadata(option.metadata),
        option.metadata?.[SCRIPT_RUNS_ON_WIDGET_METADATA_KEY] === 'true');
    } catch {
      // A failed lookup must not throw away values the user already filled in.
      this.markUnresolved(version);
    }
  }

  private markUnresolved(version: number): void {
    if (version !== this.request) return;
    this.unresolved.set(true);
  }

  private apply(version: number, declarations: ScriptInput[], runsOnWidget: boolean): void {
    if (version !== this.request) return;
    this.unresolved.set(false);
    this.declarations.set(declarations);
    this.runsOnWidget.set(runsOnWidget);
    this.reconcile(declarations);
  }

  private reconcile(declarations: ScriptInput[]): void {
    const existing = this.block.parameters ?? [];
    const byName = new Map(existing.map(param => [param.name, param]));

    const kept = existing.filter(param => !isScriptInputParam(param.name) && param.name !== RUN_SCRIPT_WIDGET_PARAM);
    const inputs = declarations.map(input => {
      const stored = byName.get(scriptInputParamName(input.name));
      const parameter = scriptInputParameter(input, stored ? stored.value : scriptInputDefaultValue(input));
      return stored?.valueLabel === undefined ? parameter : { ...parameter, valueLabel: stored.valueLabel };
    });

    const widget = this.widgetTargetParameter(byName.get(RUN_SCRIPT_WIDGET_PARAM));
    const next = widget ? [...kept, ...inputs, widget] : [...kept, ...inputs];
    if (JSON.stringify(next) === JSON.stringify(existing)) return;
    this.store.setBlockParameters(this.block.id, next);
  }

  private widgetTargetParameter(stored: ActionBlockParameter | undefined): ActionBlockParameter | null {
    if (!this.runsOnWidget() || this.block.actionId === REMOTE_SCRIPT_ACTION_ID) return null;

    const parameter: ActionBlockParameter = {
      name: RUN_SCRIPT_WIDGET_PARAM,
      type: 'widget-target',
      label: this.localization.translateKey(AppStrings.Integrations.Widgets.Actions.TargetWidgetLabel),
      value: stored ? stored.value : (this.store.hasOwnerWidget() ? WIDGET_TARGET_SELF : ''),
      required: true,
      optionsSourceId: 'macrodeck.widgets',
    };
    return stored?.valueLabel === undefined ? parameter : { ...parameter, valueLabel: stored.valueLabel };
  }

  private stringParameter(name: string): string {
    const value = this.block.parameters?.find(param => param.name === name)?.value;
    return typeof value === 'string' ? value : '';
  }
}
