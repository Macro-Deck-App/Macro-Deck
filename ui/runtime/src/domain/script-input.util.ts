import type {
  ActionBlockParameter,
  ActionParamControlType,
  ParameterValue,
} from './action-builder.interface';
import type { ScriptInput, ScriptInputType } from './script.interface';
import type { Variable } from './variable.interface';

export const SCRIPT_INPUT_PARAM_PREFIX = 'input:';

export const SCRIPT_INPUTS_METADATA_KEY = 'scriptInputs';

export const SCRIPT_RUNS_ON_WIDGET_METADATA_KEY = 'runsOnWidget';

export const SCRIPT_CALL_SITES: Record<string, string> = {
  'app.macro-deck.scripts': 'run-script',
  'app.macro-deck.delegate': 'run-remote-script',
};

export const RUN_SCRIPT_WIDGET_PARAM = 'widget';

export function isRunScriptWidgetTargetParam(paramName: string, block: { integrationId?: string; actionId?: string } | undefined): boolean {
  return paramName === RUN_SCRIPT_WIDGET_PARAM
    && !!block?.integrationId
    && SCRIPT_CALL_SITES[block.integrationId] === block.actionId;
}

export type ScriptInputValue = string | number | boolean | null;

export function scriptInputParamName(inputName: string): string {
  return SCRIPT_INPUT_PARAM_PREFIX + inputName;
}

export function isScriptInputParam(paramName: string): boolean {
  return paramName.startsWith(SCRIPT_INPUT_PARAM_PREFIX);
}

export function scriptInputNameFromParam(paramName: string): string {
  return paramName.slice(SCRIPT_INPUT_PARAM_PREFIX.length);
}

export function scriptInputControlType(type: ScriptInputType): ActionParamControlType {
  switch (type) {
    case 'numeric':
      return 'number';
    case 'boolean':
      return 'boolean';
    default:
      return 'string';
  }
}

export function normalizeScriptInputs(value: unknown): ScriptInput[] {
  if (!Array.isArray(value)) return [];

  const inputs: ScriptInput[] = [];
  for (const entry of value) {
    const candidate = entry as Partial<ScriptInput> | null;
    if (!candidate || typeof candidate.name !== 'string' || candidate.name === '') continue;

    const input: ScriptInput = {
      name: candidate.name,
      type: isScriptInputType(candidate.type) ? candidate.type : 'text',
    };
    if (typeof candidate.label === 'string') input.label = candidate.label;
    if (typeof candidate.description === 'string') input.description = candidate.description;
    if (typeof candidate.required === 'boolean') input.required = candidate.required;
    if (typeof candidate.defaultValue === 'string') input.defaultValue = candidate.defaultValue;

    inputs.push(input);
  }
  return inputs;
}

export function parseScriptInputsMetadata(metadata: Record<string, string> | undefined): ScriptInput[] {
  const encoded = metadata?.[SCRIPT_INPUTS_METADATA_KEY];
  if (!encoded) return [];

  try {
    return normalizeScriptInputs(JSON.parse(encoded));
  } catch {
    // A malformed option must not take the whole card down; no declarations is the honest fallback.
    return [];
  }
}

export function scriptInputVariables(inputs: ScriptInput[] | undefined): Variable[] {
  if (!inputs) return [];
  return inputs
    .filter(input => input.name !== '')
    .map(input => ({
      id: `input:${input.name}`,
      name: input.name,
      scope: 'global' as const,
      type: input.type,
      classification: 'user' as const,
      value: input.defaultValue ?? '',
      origin: 'input' as const,
    }));
}

export function scriptInputDefaultValue(input: ScriptInput): ParameterValue {
  const declared = input.defaultValue;
  switch (input.type) {
    case 'boolean':
      return declared === 'true';
    case 'numeric':
      return declared !== undefined && declared !== '' && Number.isFinite(Number(declared))
        ? Number(declared)
        : '';
    default:
      return declared ?? '';
  }
}

export function scriptInputParameter(input: ScriptInput, value: ParameterValue): ActionBlockParameter {
  const parameter: ActionBlockParameter = {
    name: scriptInputParamName(input.name),
    type: scriptInputControlType(input.type),
    label: input.label || input.name,
    value,
    required: input.required === true && input.defaultValue === undefined,
    acceptedVariableTypes: [input.type],
  };
  if (input.description) parameter.description = input.description;
  return parameter;
}

function isScriptInputType(value: unknown): value is ScriptInputType {
  return value === 'text' || value === 'numeric' || value === 'boolean';
}
