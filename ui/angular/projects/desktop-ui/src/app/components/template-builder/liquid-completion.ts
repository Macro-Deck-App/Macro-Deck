import { matchesVariableTokenQuery, variableTokenKind, variableTokenLabel } from '@macro-deck/runtime';
import type { Variable } from '@macro-deck/runtime';
import { isInsideLiquidTag } from './liquid-context.util';
import { LIQUID_CONTROL_SNIPPETS, LIQUID_FILTERS } from './liquid-snippets';

export type LiquidCompletionKind = 'variable' | 'filter' | 'control';

export interface LiquidCompletionItem {
  kind: LiquidCompletionKind;
  label: string;
  insert: string;
  caret?: number;
  detail?: string;
  hintKey?: string;
}

export interface LiquidCompletions {
  from: number;
  to: number;
  items: LiquidCompletionItem[];
}

const MAX_VARIABLES = 50;
const CONTROL_TRIGGER = /\{%-?[ \t]*([A-Za-z_]*)$/;
const FILTER_TRIGGER = /\|[ \t]*([A-Za-z_]*)$/;
const PREFIXED_VARIABLE_TRIGGER = /\b(vars|event)\.([A-Za-z0-9_]*)$/;
const OPENED_OUTPUT = /\{\{-?[ \t]*$/;
const WORD_TRIGGER = /(?:^|[\s(=<>!,:])([A-Za-z_][A-Za-z0-9_]*)$/;
const KEYWORDS = new Set([
  'and', 'or', 'not', 'contains', 'in', 'true', 'false', 'nil', 'null', 'empty', 'blank',
  'else', 'elsif', 'when', 'with', 'reversed', 'limit', 'offset',
]);
const CLOSED_AFTER = /^[ \t]*-?\}\}/;

export function liquidCompletions(doc: string, pos: number, variables: readonly Variable[]): LiquidCompletions | null {
  const before = doc.slice(0, pos);

  const control = CONTROL_TRIGGER.exec(before);
  if (control) {
    const query = control[1].toLowerCase();
    return {
      from: control.index,
      to: pos,
      items: LIQUID_CONTROL_SNIPPETS
        .filter(snippet => snippet.key.includes(query) || snippet.label.toLowerCase().includes(query))
        .map(snippet => ({
          kind: 'control',
          label: snippet.label,
          insert: snippet.insert,
          caret: snippet.caret,
          detail: snippet.example,
          hintKey: snippet.hintKey,
        })),
    };
  }

  if (!isInsideLiquidTag(doc, pos)) {
    return null;
  }

  const filter = FILTER_TRIGGER.exec(before);
  if (filter) {
    const query = filter[1].toLowerCase();
    return {
      from: pos - filter[1].length,
      to: pos,
      items: LIQUID_FILTERS
        .filter(snippet => snippet.key.includes(query))
        .map(snippet => ({
          kind: 'filter',
          label: snippet.label,
          insert: snippet.insert.replace(/^ \| /, '').trimStart(),
          hintKey: snippet.hintKey,
        })),
    };
  }

  const inOutput = before.lastIndexOf('{{') > before.lastIndexOf('{%');
  const closed = !inOutput || CLOSED_AFTER.test(doc.slice(pos));
  const prefixed = PREFIXED_VARIABLE_TRIGGER.exec(before);
  if (prefixed) {
    const kind = prefixed[1] === 'event' ? 'event' : 'variable';
    return {
      from: prefixed.index,
      to: pos,
      items: variableItems(variables, prefixed[2], kind, closed),
    };
  }

  const word = WORD_TRIGGER.exec(before);
  if (word && KEYWORDS.has(word[1].toLowerCase())) {
    return null;
  }

  if (word || (inOutput && OPENED_OUTPUT.test(before))) {
    const query = word?.[1] ?? '';
    return {
      from: pos - query.length,
      to: pos,
      items: variableItems(variables, query, null, closed),
    };
  }

  return null;
}

function variableItems(
  variables: readonly Variable[],
  query: string,
  kind: 'variable' | 'event' | null,
  closed: boolean,
): LiquidCompletionItem[] {
  const items: LiquidCompletionItem[] = [];
  for (const variable of variables) {
    if (items.length >= MAX_VARIABLES) break;
    const variableKind = variableTokenKind(variable);
    if (kind && kind !== variableKind) continue;
    if (!matchesVariableTokenQuery(variableKind, variable.name, query)) continue;
    const label = variableTokenLabel(variableKind, variable.name);
    items.push({
      kind: 'variable',
      label,
      insert: closed ? label : `${label} }}`,
      detail: variableKind === 'event' ? variable.type : variable.value,
    });
  }
  return items;
}
