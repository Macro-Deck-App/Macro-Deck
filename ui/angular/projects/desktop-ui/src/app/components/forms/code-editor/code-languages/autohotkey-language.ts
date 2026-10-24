import type { IndentContext, StreamParser, StringStream } from '@codemirror/language';
import type { Extension } from '@codemirror/state';

const KEYWORDS = new Set([
  'if', 'else', 'loop', 'while', 'until', 'for', 'in', 'break', 'continue', 'return', 'goto',
  'try', 'catch', 'finally', 'throw', 'switch', 'case', 'default', 'class', 'extends', 'static',
  'global', 'local', 'new', 'super', 'this', 'and', 'or', 'not', 'is', 'as', 'contains', 'unset',
]);

const BOOL_KEYWORDS = new Set(['true', 'false']);

const HOTSTRING = /^:([^:\s]*):[^:]+?::/;
const HOTKEY = /^[~*$!^+#<>]*(?:[^\s:;"'`(){}=,]+)(?:\s+&\s+[~*$]*[^\s:;"'`(){}=,]+)?(?:\s+up)?::/i;
const OPERATOR = /^(?::=|\+=|-=|\*=|\/=|\.=|\|\|=|&&|\|\||\?\?=?|==|!==?|<=|>=|=>|<<|>>>?|\*\*|\+\+|--|[+\-*/.<>=!&|^~?:])/;

export interface AutoHotkeyState {
  blockComment: boolean;
  literalLine: boolean;
  depth: number;
}

function precededBySpace(stream: StringStream): boolean {
  return stream.pos === 0 || /\s/.test(stream.string.charAt(stream.pos - 1));
}

function lineStart(stream: StringStream): boolean {
  return stream.string.slice(0, stream.pos).trim() === '';
}

export const autohotkeyStreamParser: StreamParser<AutoHotkeyState> = {
  name: 'autohotkey',
  startState: (): AutoHotkeyState => ({ blockComment: false, literalLine: false, depth: 0 }),
  token(stream: StringStream, state: AutoHotkeyState): string | null {
    if (stream.sol()) {
      state.literalLine = false;
    }

    if (state.blockComment) {
      if (stream.match(/^.*?\*\//)) {
        state.blockComment = false;
      } else {
        stream.skipToEnd();
      }
      return 'comment';
    }

    if (stream.eatSpace()) {
      return null;
    }

    if (stream.peek() === ';' && precededBySpace(stream)) {
      stream.skipToEnd();
      return 'comment';
    }

    if (state.literalLine) {
      stream.match(/^.*?(?=\s;|$)/);
      return 'string';
    }

    if (lineStart(stream)) {
      if (stream.match('/*')) {
        state.blockComment = !stream.match(/^.*?\*\//);
        if (state.blockComment) {
          stream.skipToEnd();
        }
        return 'comment';
      }

      const hotstring = stream.match(HOTSTRING) as RegExpMatchArray | null;
      if (hotstring) {
        state.literalLine = !/x/i.test(hotstring[1]);
        return 'labelName';
      }

      if (stream.match(HOTKEY)) {
        return 'labelName';
      }

      if (stream.match(/^#[A-Za-z]+/)) {
        return 'meta';
      }
    }

    if (stream.match(/^"(?:[^"`]|`.)*"?/) || stream.match(/^'(?:[^'`]|`.)*'?/)) {
      return 'string';
    }

    if (stream.match(/^0x[0-9a-f]+/i) || stream.match(/^\d+(?:\.\d+)?(?:e[+-]?\d+)?/i)) {
      return 'number';
    }

    if (stream.match(/^[A-Za-z_][A-Za-z0-9_]*/)) {
      const word = stream.current();
      const lower = word.toLowerCase();
      if (/^a_/i.test(word)) return 'atom';
      if (BOOL_KEYWORDS.has(lower)) return 'bool';
      if (KEYWORDS.has(lower)) return 'keyword';
      return 'variableName';
    }

    if (stream.match(/^[{[(]/)) {
      if (stream.current() === '{') state.depth++;
      return 'bracket';
    }

    if (stream.match(/^[}\])]/)) {
      if (stream.current() === '}') state.depth = Math.max(0, state.depth - 1);
      return 'bracket';
    }

    if (stream.match(OPERATOR)) {
      return 'operator';
    }

    if (stream.match(/^[,`]/)) {
      return 'punctuation';
    }

    stream.next();
    return null;
  },
  indent(state: AutoHotkeyState, textAfter: string, context: IndentContext): number | null {
    const depth = /^\s*\}/.test(textAfter) ? state.depth - 1 : state.depth;
    return Math.max(0, depth) * context.unit;
  },
  languageData: {
    commentTokens: { line: ';', block: { open: '/*', close: '*/' } },
    indentOnInput: /^\s*\}$/,
  },
};

export function createAutoHotkeyLanguage(language: typeof import('@codemirror/language')): Extension {
  return language.StreamLanguage.define(autohotkeyStreamParser);
}
