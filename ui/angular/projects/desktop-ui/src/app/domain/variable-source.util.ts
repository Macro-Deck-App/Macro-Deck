import { AppStrings } from '@macro-deck/runtime';
import type { Variable, VariableType } from '@macro-deck/runtime';

export const VARIABLE_TYPE_LABEL_KEYS: Record<VariableType, string> = {
  text: AppStrings.Variables.Manager.TypeText,
  numeric: AppStrings.Variables.Manager.TypeNumeric,
  boolean: AppStrings.Variables.Manager.TypeBoolean,
};

export function variableTypeLabels(translate: (key: string) => string): Record<VariableType, string> {
  return {
    text: translate(VARIABLE_TYPE_LABEL_KEYS.text),
    numeric: translate(VARIABLE_TYPE_LABEL_KEYS.numeric),
    boolean: translate(VARIABLE_TYPE_LABEL_KEYS.boolean),
  };
}

export type VariableSourceFilter =
  | { kind: 'all' }
  | { kind: 'user' }
  | { kind: 'scope' }
  | { kind: 'event' }
  | { kind: 'integration'; integrationId: string };

export function isEventParameter(variable: Variable): boolean {
  return variable.origin === 'event';
}

export function isScriptInput(variable: Variable): boolean {
  return variable.origin === 'input';
}

export function isScopeLocal(variable: Variable): boolean {
  return !isEventParameter(variable) && !isScriptInput(variable) && variable.scope !== 'global';
}

export function matchesVariableSource(variable: Variable, source: VariableSourceFilter): boolean {
  switch (source.kind) {
    case 'all':
      return true;
    case 'user':
      return !isEventParameter(variable) &&
        !isScriptInput(variable) &&
        variable.scope === 'global' &&
        variable.classification === 'user';
    case 'scope':
      return isScopeLocal(variable);
    case 'event':
      return isEventParameter(variable);
    case 'integration':
      return !isEventParameter(variable) && !isScriptInput(variable) &&
        variable.ownerIntegrationId === source.integrationId;
  }
}

export interface VariableGroup {
  key: string;
  label: string;
  variables: Variable[];
}

export type VariableSourceTranslator = (key: string) => string;

export function groupVariablesBySource(
  variables: Variable[],
  scopeLabel: string,
  integrationName: (integrationId: string) => string,
  t: VariableSourceTranslator,
): VariableGroup[] {
  const eventParams: Variable[] = [];
  const scriptInputs: Variable[] = [];
  const locals: Variable[] = [];
  const user: Variable[] = [];
  const byIntegration = new Map<string, Variable[]>();
  const other: Variable[] = [];

  for (const variable of variables) {
    if (isEventParameter(variable)) {
      eventParams.push(variable);
    } else if (isScriptInput(variable)) {
      scriptInputs.push(variable);
    } else if (isScopeLocal(variable)) {
      locals.push(variable);
    } else if (variable.classification === 'user') {
      user.push(variable);
    } else if (variable.ownerIntegrationId) {
      const list = byIntegration.get(variable.ownerIntegrationId);
      if (list) {
        list.push(variable);
      } else {
        byIntegration.set(variable.ownerIntegrationId, [variable]);
      }
    } else {
      other.push(variable);
    }
  }

  const groups: VariableGroup[] = [];
  if (eventParams.length > 0) {
    groups.push({ key: 'event', label: t(AppStrings.Variables.Source.ThisEvent), variables: eventParams });
  }
  if (scriptInputs.length > 0) {
    groups.push({ key: 'input', label: t(AppStrings.Variables.Source.ThisScript), variables: scriptInputs });
  }
  if (locals.length > 0) {
    groups.push({ key: 'scope', label: scopeLabel, variables: locals });
  }
  if (user.length > 0) {
    groups.push({ key: 'user', label: t(AppStrings.Variables.Source.User), variables: user });
  }
  const integrationGroups = [...byIntegration.entries()]
    .map(([integrationId, list]) => ({
      key: `integration:${integrationId}`,
      label: integrationName(integrationId),
      variables: list,
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
  groups.push(...integrationGroups);
  if (other.length > 0) {
    groups.push({ key: 'other', label: t(AppStrings.Variables.Source.Other), variables: other });
  }
  return groups;
}

export interface IntegrationSourceEntry {
  integrationId: string;
  name: string;
  count: number;
}

export function integrationSourceEntries(
  variables: Variable[],
  integrationName: (integrationId: string) => string,
  catalogIntegrationIds: readonly string[] = [],
): IntegrationSourceEntry[] {
  const counts = new Map<string, number>();
  for (const integrationId of catalogIntegrationIds) {
    counts.set(integrationId, counts.get(integrationId) ?? 0);
  }
  for (const variable of variables) {
    if (variable.scope === 'global' && variable.ownerIntegrationId && !isScriptInput(variable)) {
      counts.set(variable.ownerIntegrationId, (counts.get(variable.ownerIntegrationId) ?? 0) + 1);
    }
  }
  return [...counts.entries()]
    .map(([integrationId, count]) => ({
      integrationId,
      name: integrationName(integrationId),
      count,
    }))
    .sort((a, b) => a.name.localeCompare(b.name));
}
