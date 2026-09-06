import type { Variable } from './variable.interface';

export type VariableTokenKind = 'variable' | 'event';

export interface VariableTokenSegment {
  kind: VariableTokenKind;
  name: string;
  raw: string;
}

export interface VariableTextSegment {
  kind: 'text';
  text: string;
}

export type VariableValueSegment = VariableTextSegment | VariableTokenSegment;

const TOKEN_SOURCE = '\\{\\{[ \\t]*(vars|event)\\.([A-Za-z0-9_]+)[ \\t]*\\}\\}';

export function variableTokenPattern(): RegExp {
  return new RegExp(TOKEN_SOURCE, 'g');
}

export function variableTokenLabel(kind: VariableTokenKind, name: string): string {
  return `${kind === 'event' ? 'event' : 'vars'}.${name}`;
}

export function variableTokenText(kind: VariableTokenKind, name: string): string {
  return `{{ ${variableTokenLabel(kind, name)} }}`;
}

export function variableTokenKind(variable: Pick<Variable, 'origin'>): VariableTokenKind {
  return variable.origin === 'event' ? 'event' : 'variable';
}

export function parseVariableSegments(value: string): VariableValueSegment[] {
  const segments: VariableValueSegment[] = [];
  const pattern = variableTokenPattern();
  let index = 0;

  for (let match = pattern.exec(value); match !== null; match = pattern.exec(value)) {
    if (match.index > index) {
      segments.push({ kind: 'text', text: value.slice(index, match.index) });
    }
    segments.push({
      kind: match[1] === 'event' ? 'event' : 'variable',
      name: match[2] ?? '',
      raw: match[0],
    });
    index = match.index + match[0].length;
  }

  if (index < value.length) {
    segments.push({ kind: 'text', text: value.slice(index) });
  }
  return segments;
}

export function hasVariableToken(value: string): boolean {
  return variableTokenPattern().test(value);
}

export interface VariableTokenTrigger {
  start: number;
  query: string;
  kind: VariableTokenKind | null;
}

const TRIGGER_PATTERN = /\{\{[ \t]*(?:(vars|event)\.)?([A-Za-z0-9_]*)$/;

export function findVariableTokenTrigger(textBeforeCaret: string): VariableTokenTrigger | null {
  const match = TRIGGER_PATTERN.exec(textBeforeCaret);
  if (!match) return null;
  const prefix = match[1];
  return {
    start: match.index,
    query: match[2] ?? '',
    kind: prefix === 'vars' ? 'variable' : prefix === 'event' ? 'event' : null,
  };
}

export function matchesVariableTokenQuery(
  kind: VariableTokenKind,
  name: string,
  query: string,
): boolean {
  if (!query) return true;
  const needle = query.toLowerCase();
  return name.toLowerCase().includes(needle)
    || variableTokenLabel(kind, name).toLowerCase().includes(needle);
}

export function soleVariableToken(value: string): VariableTokenSegment | null {
  const segments = parseVariableSegments(value);
  const only = segments.length === 1 ? segments[0] : undefined;
  return only && only.kind !== 'text' ? only : null;
}
