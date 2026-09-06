export type ActionBlockType =
  | 'trigger'
  | 'action'
  | 'condition'
  | 'loop'
  | 'delay'
  | 'flow-control';

export interface VariableReference {
  $var: string;
}

export interface SecretReference {
  $secret: string;
}

export interface EventReference {
  $event: string;
}

export interface HotkeyValue {
  modifiers: string[];
  key: string;
  code: string;
}

export type ParameterValue =
  | string
  | number
  | boolean
  | null
  | string[]
  | VariableReference
  | SecretReference
  | EventReference
  | HotkeyValue
  | Record<string, unknown>
  | ParameterValue[];

export function isVariableReference(value: unknown): value is VariableReference {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as { $var?: unknown }).$var === 'string'
  );
}

export function isEventReference(value: unknown): value is EventReference {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as { $event?: unknown }).$event === 'string'
  );
}

export function isSecretReference(value: unknown): value is SecretReference {
  return (
    typeof value === 'object' &&
    value !== null &&
    typeof (value as { $secret?: unknown }).$secret === 'string'
  );
}

export function isHotkeyValue(value: unknown): value is HotkeyValue {
  return (
    typeof value === 'object' &&
    value !== null &&
    Array.isArray((value as { modifiers?: unknown }).modifiers) &&
    typeof (value as { key?: unknown }).key === 'string'
  );
}

export type ActionParamControlType =
  | 'string'
  | 'number'
  | 'boolean'
  | 'choice'
  | 'password'
  | 'secret'
  | 'dynamic-choice'
  | 'autocomplete'
  | 'multiselect'
  | 'color'
  | 'file'
  | 'folder'
  | 'hotkey'
  | 'duration'
  | 'datetime'
  | 'json'
  | 'code'
  | 'keyvalue'
  | 'object'
  | 'array'
  | 'ipaddress'
  | 'url'
  | 'icon'
  | 'image'
  | 'keyboard-sequence'
  | 'keyboard-combo'
  | 'widget-target';

export const WIDGET_TARGET_SELF = '$self';

export const WIDGET_INTEGRATION_ID = 'app.macro-deck.widget';

export const WIDGET_APPEARANCE_RESET = '$reset';

// Not every `set-*` action of the widget integration is an appearance action: `set-state` writes the
// active state and takes no appearance field at all.
export const WIDGET_APPEARANCE_ACTION_IDS = [
  'set-label',
  'set-background-color',
  'set-label-color',
  'set-icon',
  'set-icon-display',
  'set-font',
  'set-border',
] as const;

export function isWidgetAppearanceActionId(actionId: string | undefined): boolean {
  return actionId !== undefined && (WIDGET_APPEARANCE_ACTION_IDS as readonly string[]).includes(actionId);
}

export interface ActionBlockParameter {
  name: string;
  type: ActionParamControlType;
  value: ParameterValue;
  label: string;
  description?: string;
  placeholder?: string;
  autoPrefixHttps?: boolean;
  defaultValue?: ParameterValue;
  required?: boolean;
  multiline?: boolean;
  supportsReset?: boolean;
  literalOnly?: boolean;
  validationRegex?: string;
  maxLength?: number;
  options?: { label: string; value: ParameterValue }[];
  min?: number;
  max?: number;
  step?: number;
  showSlider?: boolean;
  dynamicOptions?: boolean;
  optionsSourceId?: string;
  allowSelf?: boolean;
  widgetTypes?: string[];
  fileExtensions?: string[];
  language?: string;
  children?: Omit<ActionBlockParameter, 'value'>[];
  itemTemplate?: Omit<ActionBlockParameter, 'value'>;

  visibleWhen?: { parameterName: string; values: string[] };

  acceptedVariableTypes?: ('text' | 'numeric' | 'boolean')[];

  valueLabel?: string;

  operator?: ComparisonOperator;
}

export type ComparisonOperator =
  | '=='
  | '!='
  | '>'
  | '<'
  | '>='
  | '<='
  | 'contains'
  | 'startsWith'
  | 'isEmpty'
  | 'isNotEmpty'
  | 'isAvailable'
  | 'isNotAvailable';

const STATE_OPERATOR_VALUES: ReadonlySet<string> = new Set([
  'isEmpty',
  'isNotEmpty',
  'isAvailable',
  'isNotAvailable',
]);

export function isStateOperator(op: ComparisonOperator | string | undefined): boolean {
  return op !== undefined && STATE_OPERATOR_VALUES.has(op);
}

export interface ComparisonExpression {
  kind: 'compare';
  id: string;
  left: ParameterValue;
  operator: ComparisonOperator;
  right: ParameterValue;
}

export interface LogicalExpression {
  kind: 'and' | 'or';
  id: string;
  operands: ConditionExpression[];
}

export type ConditionExpression = ComparisonExpression | LogicalExpression;

export function isComparisonExpression(value: unknown): value is ComparisonExpression {
  return (
    typeof value === 'object' &&
    value !== null &&
    (value as { kind?: unknown }).kind === 'compare'
  );
}

export function isLogicalExpression(value: unknown): value is LogicalExpression {
  if (typeof value !== 'object' || value === null) return false;
  const k = (value as { kind?: unknown }).kind;
  return k === 'and' || k === 'or';
}

export interface ActionBranch {
  id: string;
  kind: 'if' | 'elseif' | 'else';
  condition?: ConditionExpression;
  children: ActionBlock[];
}

export interface ActionBlock {
  id: string;
  type: ActionBlockType;
  blockType: string;
  label: string;
  color: string;
  integrationId?: string;
  actionId?: string;
  parameters?: ActionBlockParameter[];

  disabled?: boolean;

  comment?: string;

  condition?: ConditionExpression;

  children?: ActionBlock[];

  branches?: ActionBranch[];
}

export interface ActionBlockDefinition {
  blockType: string;
  type: ActionBlockType;
  label: string;
  description?: string;
  color: string;
  icon?: string;
  category: string;
  categoryId?: string;
  integrationId?: string;
  actionId?: string;
  descriptiveUiSchema?: string;
  blockShape?: 'hat' | 'stack' | 'c-block' | 'cap';
  parameters?: Omit<ActionBlockParameter, 'value'>[];
  providesButtonState?: boolean;
  providesWidgetIcon?: boolean;
  hasChildren?: boolean;
  hasBranches?: boolean;
  hasCondition?: boolean;
  loopOnly?: boolean;
}

export const EVENT_TRIGGER_TYPE = 'onEvent';

export interface EventTriggerBinding {
  providerId: string;
  eventId: string;
  eventName?: string;
  parameters?: ActionBlockParameter[];
  filter?: ConditionExpression;
}

export interface ActionFlow {
  triggerId: string;
  triggerType: string;
  triggerLabel?: string;
  event?: EventTriggerBinding;
  children: ActionBlock[];
}

export function qualifiedEventId(binding: EventTriggerBinding | undefined): string | undefined {
  if (!binding?.providerId || !binding.eventId) return undefined;
  return `${binding.providerId}::${binding.eventId}`;
}

export function eventConfigurationValues(parameters: ActionBlockParameter[] | undefined): Record<string, unknown> {
  const values: Record<string, unknown> = {};
  for (const param of parameters ?? []) {
    values[param.name] = param.value;
  }
  return values;
}

export function isEventFlow(flow: ActionFlow): boolean {
  return flow.triggerType === EVENT_TRIGGER_TYPE;
}

export function isSameTriggerType(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}
