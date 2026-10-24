import { StringStream } from '@codemirror/language';

import { AutoHotkeyState, autohotkeyStreamParser } from './autohotkey-language';

type Token = [text: string, type: string | null];

function tokenize(source: string): Token[][] {
  const parser = autohotkeyStreamParser;
  const state = parser.startState!(2) as AutoHotkeyState;
  return source.split('\n').map(line => {
    const tokens: Token[] = [];
    const stream = new StringStream(line, 4, 2);
    if (line === '') {
      parser.blankLine?.(state, 2);
      return tokens;
    }
    while (!stream.eol()) {
      const type = parser.token(stream, state);
      const text = stream.current();
      if (text.trim() !== '') tokens.push([text, type]);
      stream.start = stream.pos;
    }
    return tokens;
  });
}

function typeOf(source: string, text: string): string | null | undefined {
  return tokenize(source).flat().find(([t]) => t === text)?.[1];
}

describe('autohotkeyStreamParser', () => {
  it('treats ; after whitespace as a line comment but not inside a string', () => {
    const [line] = tokenize('x := "a;b" ; note');
    expect(line).toEqual([
      ['x', 'variableName'],
      [':=', 'operator'],
      ['"a;b"', 'string'],
      ['; note', 'comment'],
    ]);
  });

  it('spans /* */ block comments over lines', () => {
    const lines = tokenize('/* first\nMsgBox "x"\n*/\nMsgBox "y"');
    expect(lines[0]).toEqual([['/* first', 'comment']]);
    expect(lines[1]).toEqual([['MsgBox "x"', 'comment']]);
    expect(lines[2]).toEqual([['*/', 'comment']]);
    expect(lines[3]).toEqual([['MsgBox', 'variableName'], ['"y"', 'string']]);
  });

  it('keeps backtick-escaped quotes inside double and single quoted strings', () => {
    expect(typeOf('s := "say `"hi`" now"', '"say `"hi`" now"')).toBe('string');
    expect(typeOf("s := 'it`'s'", "'it`'s'")).toBe('string');
  });

  it('marks keywords case-insensitively, booleans and A_ built-in variables', () => {
    const [line] = tokenize('If (A_Index > 3 && true) Return');
    expect(line).toContain(['If', 'keyword']);
    expect(line).toContain(['A_Index', 'atom']);
    expect(line).toContain(['true', 'bool']);
    expect(line).toContain(['Return', 'keyword']);
    expect(line).toContain(['3', 'number']);
  });

  it('marks #directives and hotkey labels at the start of a line', () => {
    expect(typeOf('#Requires AutoHotkey v2.0', '#Requires')).toBe('meta');
    expect(typeOf('^!a::MsgBox "x"', '^!a::')).toBe('labelName');
    expect(typeOf('#n::Run "notepad"', '#n::')).toBe('labelName');
    expect(typeOf('~LButton & RButton::', '~LButton & RButton::')).toBe('labelName');
    expect(typeOf('x := y ? a : b', 'x')).toBe('variableName');
  });

  it('marks hotstring labels and their literal replacement text unless the X option is set', () => {
    expect(tokenize(':*:btw::by the way')[0]).toEqual([[':*:btw::', 'labelName'], ['by the way', 'string']]);
    expect(tokenize(':X:now::Send A_Now')[0]).toEqual([
      [':X:now::', 'labelName'],
      ['Send', 'variableName'],
      ['A_Now', 'atom'],
    ]);
  });

  it('reads hex and decimal numbers', () => {
    expect(typeOf('n := 0x1F', '0x1F')).toBe('number');
    expect(typeOf('n := 1.5e3', '1.5e3')).toBe('number');
  });

  it('indents one unit inside braces and dedents a closing brace', () => {
    const parser = autohotkeyStreamParser;
    const state = parser.startState!(2) as AutoHotkeyState;
    const stream = new StringStream('if (x) {', 4, 2);
    while (!stream.eol()) {
      parser.token(stream, state);
      stream.start = stream.pos;
    }
    const context = { unit: 2 } as Parameters<NonNullable<typeof parser.indent>>[2];
    expect(parser.indent!(state, 'MsgBox', context)).toBe(2);
    expect(parser.indent!(state, '}', context)).toBe(0);
  });
});
