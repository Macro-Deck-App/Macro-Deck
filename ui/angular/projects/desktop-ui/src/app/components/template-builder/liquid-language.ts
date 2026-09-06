import type { HighlightStyle as HighlightStyleType, StringStream } from '@codemirror/language';
import type { Extension } from '@codemirror/state';

const KEYWORDS = new Set([
  'if', 'elsif', 'else', 'endif', 'unless', 'endunless', 'for', 'endfor', 'in',
  'case', 'when', 'endcase', 'assign', 'capture', 'endcapture', 'break', 'continue',
  'comment', 'endcomment', 'and', 'or', 'contains',
]);

const BOOL_KEYWORDS = new Set(['true', 'false', 'nil', 'null']);

interface LiquidState {
  tag: 'output' | 'statement' | null;
}

export function createLiquidLanguage(language: typeof import('@codemirror/language')): Extension {
  const { StreamLanguage } = language;
  return StreamLanguage.define<LiquidState>({
    startState: (): LiquidState => ({ tag: null }),
    token(stream: StringStream, state: LiquidState): string | null {
      if (state.tag === null) {
        if (stream.match('{{')) {
          state.tag = 'output';
          return 'bracket';
        }
        if (stream.match('{%')) {
          state.tag = 'statement';
          return 'bracket';
        }
        stream.next();
        return null;
      }

      if (state.tag === 'output' && stream.match('}}')) {
        state.tag = null;
        return 'bracket';
      }
      if (state.tag === 'statement' && stream.match('%}')) {
        state.tag = null;
        return 'bracket';
      }

      if (stream.eatSpace()) return null;

      if (stream.match(/^"(?:[^"\\]|\\.)*"?/) || stream.match(/^'(?:[^'\\]|\\.)*'?/)) {
        return 'string';
      }

      if (stream.match(/^-?\d+(\.\d+)?/)) {
        return 'number';
      }

      if (stream.match('|')) {
        return 'operator';
      }

      if (stream.match(/^(vars|event)\b/)) {
        return 'atom';
      }

      if (stream.match(/^[A-Za-z_][A-Za-z0-9_]*/)) {
        const word = stream.current();
        if (BOOL_KEYWORDS.has(word)) return 'bool';
        if (KEYWORDS.has(word)) return 'keyword';
        // Right after a `|`, an identifier names a filter; otherwise it is a plain member/variable
        // name - both render the same here, so no lookbehind bookkeeping is needed.
        return 'variableName';
      }

      if (stream.match(/^[.:,()[\]]/)) {
        return 'punctuation';
      }

      stream.next();
      return null;
    },
  });
}

export function createLiquidHighlightStyle(
  language: typeof import('@codemirror/language'),
  highlight: typeof import('@lezer/highlight'),
): HighlightStyleType {
  const { tags } = highlight;
  return language.HighlightStyle.define([
    { tag: [tags.bracket, tags.punctuation], color: 'var(--color-text-secondary)' },
    { tag: tags.operator, color: 'var(--color-text-secondary)' },
    { tag: tags.atom, color: 'var(--color-accent)' },
    { tag: tags.keyword, color: 'var(--color-accent)', fontWeight: 'var(--font-semibold)' },
    { tag: tags.bool, color: 'var(--type-boolean-fg)' },
    { tag: tags.string, color: 'var(--type-string-fg)' },
    { tag: tags.number, color: 'var(--type-numeric-fg)' },
    { tag: tags.variableName, color: 'var(--color-text-primary)' },
  ]);
}
