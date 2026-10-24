import type { HighlightStyle as HighlightStyleType } from '@codemirror/language';
import type { Extension } from '@codemirror/state';

import { createLiquidLanguage } from '../../../template-builder/liquid-language';
import { createAutoHotkeyLanguage } from './autohotkey-language';

export type CodeLanguageMode = 'text' | 'json' | 'liquid' | 'autohotkey';

export interface CodeLanguageModules {
  language: typeof import('@codemirror/language');
  langJson: typeof import('@codemirror/lang-json');
}

export function codeLanguageMode(language: string | null | undefined): CodeLanguageMode {
  switch ((language ?? '').trim().toLowerCase()) {
    case 'json':
      return 'json';
    case 'liquid':
      return 'liquid';
    case 'autohotkey':
      return 'autohotkey';
    default:
      return 'text';
  }
}

export function codeLanguageExtension(mode: CodeLanguageMode, modules: CodeLanguageModules): Extension {
  switch (mode) {
    case 'json':
      return modules.langJson.json();
    case 'liquid':
      return createLiquidLanguage(modules.language);
    case 'autohotkey':
      return createAutoHotkeyLanguage(modules.language);
    default:
      return [];
  }
}

export function createCodeHighlightStyle(
  language: typeof import('@codemirror/language'),
  highlight: typeof import('@lezer/highlight'),
): HighlightStyleType {
  const { tags } = highlight;
  return language.HighlightStyle.define([
    { tag: tags.comment, color: 'var(--color-text-muted)', fontStyle: 'italic' },
    { tag: [tags.keyword, tags.controlKeyword], color: 'var(--color-accent)', fontWeight: 'var(--font-semibold)' },
    { tag: [tags.meta, tags.labelName], color: 'var(--color-accent)' },
    { tag: [tags.atom, tags.propertyName], color: 'var(--color-accent)' },
    { tag: [tags.bool, tags.null], color: 'var(--type-boolean-fg)' },
    { tag: tags.string, color: 'var(--type-string-fg)' },
    { tag: tags.number, color: 'var(--type-numeric-fg)' },
    {
      tag: [tags.bracket, tags.punctuation, tags.operator, tags.separator, tags.squareBracket, tags.brace],
      color: 'var(--color-text-secondary)',
    },
    { tag: tags.variableName, color: 'var(--color-text-primary)' },
  ]);
}
