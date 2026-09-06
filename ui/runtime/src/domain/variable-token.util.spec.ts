import {
  findVariableTokenTrigger,
  hasVariableToken,
  matchesVariableTokenQuery,
  parseVariableSegments,
  variableTokenKind,
  variableTokenLabel,
  variableTokenText,
} from './variable-token.util';

describe('variable-token.util', () => {
  describe('parseVariableSegments', () => {
    it('returns a single text segment for a value without references', () => {
      expect(parseVariableSegments('plain text')).toEqual([{ kind: 'text', text: 'plain text' }]);
    });

    it('splits literal text around variable and event references', () => {
      expect(parseVariableSegments('a {{ vars.volume }} b {{ event.name }}')).toEqual([
        { kind: 'text', text: 'a ' },
        { kind: 'variable', name: 'volume', raw: '{{ vars.volume }}' },
        { kind: 'text', text: ' b ' },
        { kind: 'event', name: 'name', raw: '{{ event.name }}' },
      ]);
    });

    it('keeps the exact source text so removal is lossless', () => {
      const segments = parseVariableSegments('{{vars.a}} {{  vars.b  }}');
      expect(segments.map(s => (s.kind === 'text' ? s.text : s.raw)).join('')).toBe(
        '{{vars.a}} {{  vars.b  }}');
    });

    it('leaves a filtered expression as plain text - a chip could not round-trip it', () => {
      expect(parseVariableSegments('{{ vars.a | upcase }}')).toEqual([
        { kind: 'text', text: '{{ vars.a | upcase }}' },
      ]);
    });

    it('leaves a control-flow tag and an unknown root as plain text', () => {
      expect(parseVariableSegments('{% if x %}')).toEqual([{ kind: 'text', text: '{% if x %}' }]);
      expect(parseVariableSegments('{{ other.a }}')).toEqual([
        { kind: 'text', text: '{{ other.a }}' },
      ]);
    });

    it('returns nothing for an empty value', () => {
      expect(parseVariableSegments('')).toEqual([]);
    });
  });

  describe('hasVariableToken', () => {
    it('is independent between calls - the matcher must not carry a lastIndex', () => {
      expect(hasVariableToken('{{ vars.a }}')).toBeTrue();
      expect(hasVariableToken('{{ vars.a }}')).toBeTrue();
      expect(hasVariableToken('nothing here')).toBeFalse();
    });
  });

  describe('token text', () => {
    it('writes the canonical form for both roots', () => {
      expect(variableTokenText('variable', 'volume')).toBe('{{ vars.volume }}');
      expect(variableTokenText('event', 'name')).toBe('{{ event.name }}');
      expect(variableTokenLabel('variable', 'volume')).toBe('vars.volume');
      expect(variableTokenLabel('event', 'name')).toBe('event.name');
    });

    it('maps the client-only event marker onto the event root', () => {
      expect(variableTokenKind({ origin: 'event' })).toBe('event');
      expect(variableTokenKind({})).toBe('variable');
    });
  });

  describe('findVariableTokenTrigger', () => {
    it('opens on the braces alone with an empty query', () => {
      expect(findVariableTokenTrigger('hello {{')).toEqual({ start: 6, query: '', kind: null });
      expect(findVariableTokenTrigger('hello {{ ')).toEqual({ start: 6, query: '', kind: null });
    });

    it('captures a half-typed name', () => {
      expect(findVariableTokenTrigger('{{ vol')).toEqual({ start: 0, query: 'vol', kind: null });
    });

    it('narrows to a root once its prefix is committed', () => {
      expect(findVariableTokenTrigger('{{ vars.')).toEqual({ start: 0, query: '', kind: 'variable' });
      expect(findVariableTokenTrigger('{{ event.na')).toEqual({
        start: 0,
        query: 'na',
        kind: 'event',
      });
    });

    it('needs the dot before treating a root name as a prefix', () => {
      expect(findVariableTokenTrigger('{{ vars')).toEqual({ start: 0, query: 'vars', kind: null });
    });

    it('closes once the reference is complete', () => {
      expect(findVariableTokenTrigger('{{ vars.a }}')).toBeNull();
    });

    it('does not open on unrelated text, a filter pipe or across a line break', () => {
      expect(findVariableTokenTrigger('plain')).toBeNull();
      expect(findVariableTokenTrigger('{{ vars.a | up')).toBeNull();
      expect(findVariableTokenTrigger('{{\nvol')).toBeNull();
    });

    it('anchors on the last open braces', () => {
      expect(findVariableTokenTrigger('{{ vars.a }} and {{ b')).toEqual({
        start: 17,
        query: 'b',
        kind: null,
      });
    });
  });

  describe('matchesVariableTokenQuery', () => {
    it('accepts everything for an empty query', () => {
      expect(matchesVariableTokenQuery('variable', 'volume', '')).toBeTrue();
    });

    it('matches on the bare name, case-insensitively', () => {
      expect(matchesVariableTokenQuery('variable', 'volume', 'LUM')).toBeTrue();
      expect(matchesVariableTokenQuery('variable', 'volume', 'zzz')).toBeFalse();
    });

    it('matches on the qualified label so a half-typed prefix still lists everything', () => {
      expect(matchesVariableTokenQuery('variable', 'volume', 'vars')).toBeTrue();
      expect(matchesVariableTokenQuery('event', 'volume', 'vars')).toBeFalse();
      expect(matchesVariableTokenQuery('event', 'volume', 'even')).toBeTrue();
    });
  });
});
