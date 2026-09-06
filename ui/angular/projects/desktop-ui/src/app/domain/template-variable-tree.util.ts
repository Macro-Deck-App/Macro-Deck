import { variableTokenKind, variableTokenLabel } from '@macro-deck/runtime';
import type { Variable } from '@macro-deck/runtime';
import { VariableSourceTranslator, groupVariablesBySource } from './variable-source.util';

export interface VariableSubgroup {
  key: string;
  label: string;
  variables: Variable[];
}

export interface VariableProviderGroup {
  key: string;
  label: string;
  subgroups: VariableSubgroup[];
}

export type ConfigurationNameResolver = (variable: Variable) => string | null;

export function buildVariableTree(
  variables: Variable[],
  scopeLabel: string,
  integrationName: (integrationId: string) => string,
  configurationName: ConfigurationNameResolver,
  t: VariableSourceTranslator,
): VariableProviderGroup[] {
  const providerGroups = groupVariablesBySource(variables, scopeLabel, integrationName, t);
  return providerGroups.map(group => ({
    key: group.key,
    label: group.label,
    subgroups: buildConfigSubgroups(group.variables, configurationName),
  }));
}

function buildConfigSubgroups(
  variables: Variable[],
  configurationName: ConfigurationNameResolver,
): VariableSubgroup[] {
  const hasAnyConfigKey = variables.some(v => !!v.configurationKey);
  if (!hasAnyConfigKey) {
    return [{ key: '', label: '', variables }];
  }

  const unlabelled: Variable[] = [];
  const byKey = new Map<string, Variable[]>();
  for (const variable of variables) {
    if (!variable.configurationKey) {
      unlabelled.push(variable);
      continue;
    }
    const list = byKey.get(variable.configurationKey);
    if (list) {
      list.push(variable);
    } else {
      byKey.set(variable.configurationKey, [variable]);
    }
  }

  const subgroups: VariableSubgroup[] = [];
  if (unlabelled.length > 0) {
    subgroups.push({ key: '', label: '', variables: unlabelled });
  }

  const configGroups = [...byKey.entries()]
    .map(([key, list]) => ({
      key,
      label: list.map(configurationName).find((name): name is string => !!name) ?? key,
      variables: list,
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
  subgroups.push(...configGroups);

  return subgroups;
}

export interface VariableLabel {
  primary: string;
  secondary: string | null;
}

export function variableLabelFor(variable: Variable, resolvedDisplayName: string): VariableLabel {
  const qualified = variableTokenLabel(variableTokenKind(variable), variable.name);
  return resolvedDisplayName
    ? { primary: resolvedDisplayName, secondary: qualified }
    : { primary: qualified, secondary: null };
}

function matchesVariable(variable: Variable, needle: string, resolvedDisplayName: string): boolean {
  const qualified = variableTokenLabel(variableTokenKind(variable), variable.name);
  return resolvedDisplayName.toLowerCase().includes(needle)
    || variable.name.toLowerCase().includes(needle)
    || qualified.toLowerCase().includes(needle);
}

export function filterVariableTree(
  groups: VariableProviderGroup[],
  query: string,
  resolvedDisplayNameOf: (variable: Variable) => string,
): VariableProviderGroup[] {
  const needle = query.trim().toLowerCase();
  if (!needle) return groups;

  const result: VariableProviderGroup[] = [];
  for (const group of groups) {
    const groupLabelMatches = group.label.toLowerCase().includes(needle);
    const subgroups: VariableSubgroup[] = [];
    for (const subgroup of group.subgroups) {
      const subgroupMatches = groupLabelMatches || subgroup.label.toLowerCase().includes(needle);
      const variables = subgroupMatches
        ? subgroup.variables
        : subgroup.variables.filter(v => matchesVariable(v, needle, resolvedDisplayNameOf(v)));
      if (variables.length > 0) {
        subgroups.push({ ...subgroup, variables });
      }
    }
    if (subgroups.length > 0) {
      result.push({ ...group, subgroups });
    }
  }
  return result;
}

export function countVariables(groups: VariableProviderGroup[]): number {
  let count = 0;
  for (const group of groups) {
    for (const subgroup of group.subgroups) {
      count += subgroup.variables.length;
    }
  }
  return count;
}
