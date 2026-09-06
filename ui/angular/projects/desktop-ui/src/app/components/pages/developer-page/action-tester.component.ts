import {
  ChangeDetectionStrategy,
  Component,
  Input,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { ActionBlock, ActionBlockParameter, AppStrings, ParameterValue, SCRIPT_CALL_SITES, ScriptInput, defaultParameterValue, isScriptInputParam, parseScriptInputsMetadata, resolveLocalizedText, scriptInputDefaultValue, scriptInputParamName, scriptInputParameter } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { ActionFlowStore } from '../../action-builder/services/action-flow.store';
import { isParameterVisible } from '../../../domain/parameter-visibility.util';
import { ActionOptionsService } from '../../../services/action-options.service';
import { ActionDefinitionModel, ActionService } from '../../../services/action.service';
import { ButtonComponent, TranslatePipe } from '@shared';
import { ParamRowComponent } from '../../action-builder/action-card/param-list/param-row.component';

const SCRIPT_ID_PARAMETER = 'scriptId';

interface RunResult {
  success: boolean;
  durationMs: number;
  status?: string;
  error?: { code: string; message: string };
  at: Date;
}

@Component({
  selector: 'app-action-tester',
  standalone: true,
  imports: [ButtonComponent, ParamRowComponent, TranslatePipe],
  providers: [ActionFlowStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './action-tester.component.html',
  styleUrls: ['./action-tester.component.scss'],
})
export class ActionTesterComponent {
  private readonly localization = inject(LocalizationService);
  private readonly actionService = inject(ActionService);
  private readonly options = inject(ActionOptionsService);

  private readonly _action = signal<ActionDefinitionModel | null>(null);

  @Input({ required: true })
  set action(value: ActionDefinitionModel) {
    this._action.set(value);
    this.seed(value);
  }

  protected readonly currentAction = this._action.asReadonly();

  protected readonly values = signal<Record<string, ParameterValue>>({});
  protected readonly running = signal(false);
  protected readonly showErrors = signal(false);
  protected readonly result = signal<RunResult | null>(null);

  private readonly scriptInputs = signal<ScriptInput[]>([]);
  private request = 0;

  private readonly paramDefs = computed<Omit<ActionBlockParameter, 'value'>[]>(() => {
    const action = this._action();
    if (!action) return [];

    const declared = this.actionService.blockParameterDefs(action);
    const inputs = this.scriptInputs().map(input => {
      const { value: _value, ...def } = scriptInputParameter(input, null);
      return def;
    });
    return [...declared, ...inputs];
  });

  protected readonly paramRows = computed<ActionBlockParameter[]>(() => {
    const values = this.values();
    return this.paramDefs().map(def => ({
      ...def,
      value: values[def.name] ?? defaultParameterValue(def),
    }));
  });

  protected readonly visibleParamRows = computed<ActionBlockParameter[]>(() => {
    const rows = this.paramRows();
    return rows.filter(row => isParameterVisible(row, rows));
  });

  protected readonly testerBlock = computed<ActionBlock>(() => {
    const action = this._action();
    return {
      id: 'tester',
      type: 'action',
      blockType: action ? `${action.integrationId}.${action.id}` : 'tester',
      label: action?.name ?? '',
      color: 'var(--color-accent)',
      integrationId: action?.integrationId,
      actionId: action?.id,
      parameters: this.paramRows(),
    };
  });

  protected readonly missingRequired = computed(() => {
    const values = this.values();
    // Only what the user can see: a hidden required field cannot be filled in, so counting it would
    // disable Run with no field to point at.
    const visible = new Set(this.visibleParamRows().map(row => row.name));
    return this.paramDefs()
      .filter(def => visible.has(def.name))
      .filter(def => ActionTesterComponent.isMissing(def, values[def.name]));
  });

  protected readonly canRun = computed(() => !this.running() && this.missingRequired().length === 0);

  private readonly selectedScriptId = computed(() => {
    const value = this.values()[SCRIPT_ID_PARAMETER];
    return typeof value === 'string' ? value : '';
  });

  constructor() {
    // The declarations ride on the script picker's own options, the same way the flow editor gets
    // them - there is no separate endpoint to ask.
    effect(() => {
      const action = this._action();
      const scriptId = this.selectedScriptId();

      untracked(() => {
        if (!action || SCRIPT_CALL_SITES[action.integrationId ?? ''] !== action.id) {
          this.applyInputs(++this.request, []);
          return;
        }

        void this.loadScriptInputs(++this.request, action, scriptId);
      });
    });
  }

  private async loadScriptInputs(
    version: number,
    action: ActionDefinitionModel,
    scriptId: string,
  ): Promise<void> {
    if (scriptId === '') {
      this.applyInputs(version, []);
      return;
    }

    const instance = this.values()['instance'];
    try {
      const response = await this.options.getOptions({
        integrationId: action.integrationId,
        actionId: action.id,
        parameterName: SCRIPT_ID_PARAMETER,
        currentParameters: typeof instance === 'string' && instance !== '' ? { instance } : undefined,
      });
      const option = response.options.find(candidate => candidate.value === scriptId);
      this.applyInputs(version, parseScriptInputsMetadata(option?.metadata));
    } catch {
      // A failed lookup must not throw away values the user already filled in.
    }
  }

  private applyInputs(version: number, inputs: ScriptInput[]): void {
    if (version !== this.request) return;

    const wanted = new Set(inputs.map(input => scriptInputParamName(input.name)));
    const unchanged = inputs.length === this.scriptInputs().length &&
      inputs.every((input, index) => input.name === this.scriptInputs()[index]?.name &&
        input.type === this.scriptInputs()[index]?.type);
    if (unchanged) return;

    this.scriptInputs.set(inputs);
    this.values.update(current => {
      const next: Record<string, ParameterValue> = {};
      for (const [name, value] of Object.entries(current)) {
        if (!isScriptInputParam(name) || wanted.has(name)) next[name] = value;
      }
      for (const input of inputs) {
        const name = scriptInputParamName(input.name);
        if (!(name in next)) next[name] = scriptInputDefaultValue(input);
      }
      return next;
    });
  }

  protected setValue(name: string, value: ParameterValue): void {
    this.values.update(current => ({ ...current, [name]: value }));
  }

  protected fieldError(name: string): string | null {
    if (!this.showErrors()) {
      return null;
    }
    const def = this.paramDefs().find(d => d.name === name);
    if (!def) {
      return null;
    }
    return ActionTesterComponent.isMissing(def, this.values()[name])
      ? this.localization.translateKey(AppStrings.Developer.ParameterRequiredError)
      : null;
  }

  protected async run(): Promise<void> {
    const action = this._action();
    if (!action || this.running()) {
      return;
    }

    if (this.missingRequired().length > 0) {
      this.showErrors.set(true);
      return;
    }

    this.running.set(true);
    this.result.set(null);
    try {
      const response = await this.actionService.runAction(action.integrationId, action.id, this.values());
      this.result.set({
        success: response.success,
        durationMs: response.durationMs ?? 0,
        status: response.status,
        error: response.error
          ? { code: response.error.code, message: resolveLocalizedText(response.error.message, this.localization) }
          : undefined,
        at: new Date(),
      });
    } catch {
      this.result.set({
        success: false,
        durationMs: 0,
        error: {
          code: 'NETWORK_ERROR',
          message: this.localization.translateKey(AppStrings.Developer.HostUnreachableError),
        },
        at: new Date(),
      });
    } finally {
      this.running.set(false);
    }
  }

  private seed(action: ActionDefinitionModel): void {
    const values: Record<string, ParameterValue> = {};
    for (const def of this.actionService.blockParameterDefs(action)) {
      values[def.name] = defaultParameterValue(def);
    }
    this.values.set(values);
    this.result.set(null);
    this.showErrors.set(false);
  }

  private static isMissing(def: Omit<ActionBlockParameter, 'value'>, value: ParameterValue | undefined): boolean {
    if (!def.required) {
      return false;
    }
    if (value === null || value === undefined) {
      return true;
    }
    if (typeof value === 'string') {
      return value.trim() === '';
    }
    if (Array.isArray(value)) {
      return value.length === 0;
    }
    return false;
  }
}
