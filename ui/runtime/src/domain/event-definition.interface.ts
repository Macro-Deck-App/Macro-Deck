import { LocalizationTranslator, LocalizedText, resolveLocalizedText } from '../localization/localized-text';
import { ActionParameterDef } from '../protocol/messages/action';
import {
  ActionBlockParameter,
  ActionFlow,
  ActionParamControlType,
  ParameterValue,
  isEventReference,
} from './action-builder.interface';
import { defaultParameterValue } from './action-flow.util';
import { soleVariableToken } from './variable-token.util';
import type { Variable, VariableType } from './variable.interface';

export interface EventDefinition {
  id: string;
  providerId: string;
  providerName: string;
  isIntegration: boolean;
  name: string;
  description?: string;
  category?: string;
  iconName?: string;
  deliveryKind: 'push' | 'scheduled';
  configurationParameters: Omit<ActionBlockParameter, 'value'>[];
  payloadParameters: Omit<ActionBlockParameter, 'value'>[];
}

export interface EventDefinitionDto
  extends Omit<EventDefinition, 'configurationParameters' | 'payloadParameters' | 'providerName' | 'name' | 'description' | 'category'> {
  providerName: LocalizedText;
  name: LocalizedText;
  description?: LocalizedText;
  category?: LocalizedText;
  configurationParameters: ActionParameterDef[];
  payloadParameters: ActionParameterDef[];
}

export interface GetEventDefinitionsResponse {
  events: EventDefinitionDto[];
}

export interface EventProviderGroup {
  providerId: string;
  providerName: string;
  isIntegration: boolean;
  events: EventDefinition[];
}

export function groupEventsByProvider(events: EventDefinition[]): EventProviderGroup[] {
  const groups = new Map<string, EventProviderGroup>();
  for (const event of events) {
    let group = groups.get(event.providerId);
    if (!group) {
      group = {
        providerId: event.providerId,
        providerName: event.providerName,
        isIntegration: event.isIntegration,
        events: [],
      };
      groups.set(event.providerId, group);
    }
    group.events.push(event);
  }
  return [...groups.values()];
}

export function isEventFilterParameter(definition: EventDefinition, name: string): boolean {
  return definition.payloadParameters.some(param => param.name === name);
}

export function defaultEventConfigurationValue(
  definition: EventDefinition,
  declared: Omit<ActionBlockParameter, 'value'>,
): ParameterValue {
  const hasDeclaredDefault = declared.defaultValue !== undefined && declared.defaultValue !== null;
  if (!hasDeclaredDefault && isEventFilterParameter(definition, declared.name)) {
    return null;
  }
  return defaultParameterValue(declared);
}

export function eventPayloadVariables(definition: EventDefinition | undefined): Variable[] {
  if (!definition) return [];
  return definition.payloadParameters.map(param => ({
    id: `event:${definition.id}:${param.name}`,
    name: param.name,
    scope: 'global' as const,
    type: eventParameterVariableType(param.type),
    classification: 'integration' as const,
    value: '',
    origin: 'event' as const,
  }));
}

export function resolveEventPayloadParameter(
  definition: EventDefinition | undefined,
  value: ParameterValue | undefined,
): Omit<ActionBlockParameter, 'value'> | undefined {
  if (!definition) return undefined;
  const name = eventReferenceName(value);
  if (name === null) return undefined;
  return definition.payloadParameters.find(param => param.name === name);
}

function eventReferenceName(value: ParameterValue | undefined): string | null {
  if (isEventReference(value)) return value.$event;
  if (typeof value !== 'string') return null;
  const token = soleVariableToken(value);
  return token?.kind === 'event' ? token.name : null;
}

function eventParameterVariableType(type: ActionParamControlType): VariableType {
  switch (type) {
    case 'number':
    case 'duration':
      return 'numeric';
    case 'boolean':
      return 'boolean';
    default:
      return 'text';
  }
}

export function normalizeEventTriggerNames(flows: ActionFlow[], localization: LocalizationTranslator): ActionFlow[] {
  return flows.map(flow => {
    const eventName = flow.event?.eventName;
    if (eventName === undefined || typeof eventName === 'string') return flow;
    return {
      ...flow,
      event: { ...flow.event!, eventName: resolveLocalizedText(eventName as LocalizedText, localization) },
    };
  });
}
