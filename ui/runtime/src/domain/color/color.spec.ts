import { applyColorModifier, canonicalColor, type ColorModifierOp, formatColor, parseColor, type RgbaColor } from './color';
import {
  type ColorModifier,
  parseColorReference,
  resolveColorReference,
  serializeColorReference,
} from './color-reference';

type FixtureArg = number | string | { variable: string };

interface FixtureStep {
  op: ColorModifierOp;
  args: FixtureArg[];
}

interface ColorFixture {
  parse: { input: string; canonical: string | null }[];
  modifiers: (FixtureStep & { color: string; expected: string })[];
  chains: { color: string; steps: FixtureStep[]; expected: string }[];
  references: { text: string; isReference: boolean; variable?: string; steps?: FixtureStep[] }[];
}

declare const __loadColorFixture: () => unknown;

const fixture = __loadColorFixture() as ColorFixture;

function toModifier(step: FixtureStep): ColorModifier {
  if (step.op !== 'mix') return { op: step.op, amount: step.args[0] as number };
  const target = step.args[0];
  return {
    op: 'mix',
    amount: step.args[1] as number,
    mix: typeof target === 'string' ? { kind: 'color', color: target } : { kind: 'variable', variable: (target as { variable: string }).variable },
  };
}

function apply(color: RgbaColor, step: FixtureStep): RgbaColor {
  const modifier = toModifier(step);
  const mixWith = modifier.mix?.kind === 'color' ? parseColor(modifier.mix.color) ?? undefined : undefined;
  return applyColorModifier(color, modifier.op, modifier.amount, mixWith);
}

describe('color values against the shared vectors', () => {
  for (const testCase of fixture.parse) {
    it(`parses ${JSON.stringify(testCase.input)} to ${testCase.canonical}`, () => {
      expect(canonicalColor(testCase.input)).toBe(testCase.canonical);
    });
  }

  for (const testCase of fixture.modifiers) {
    it(`${testCase.op} ${JSON.stringify(testCase.args)} turns ${testCase.color} into ${testCase.expected}`, () => {
      expect(formatColor(apply(parseColor(testCase.color) as RgbaColor, testCase))).toBe(testCase.expected);
    });
  }

  for (const testCase of fixture.chains) {
    it(`applies the chain on ${testCase.color} in order to reach ${testCase.expected}`, () => {
      let color = parseColor(testCase.color) as RgbaColor;
      for (const step of testCase.steps) color = apply(color, step);
      expect(formatColor(color)).toBe(testCase.expected);
    });
  }
});

describe('color references against the shared vectors', () => {
  for (const testCase of fixture.references) {
    it(`${testCase.isReference ? 'recognises' : 'rejects'} ${JSON.stringify(testCase.text)}`, () => {
      const reference = parseColorReference(testCase.text);
      if (!testCase.isReference) {
        expect(reference).toBeNull();
        return;
      }
      expect(reference).toEqual({ variable: testCase.variable as string, modifiers: (testCase.steps ?? []).map(toModifier) });
    });
  }

  for (const testCase of fixture.references.filter(candidate => candidate.isReference)) {
    it(`writes ${JSON.stringify(testCase.text)} back in the canonical form that reads the same`, () => {
      const reference = parseColorReference(testCase.text)!;
      const text = serializeColorReference(reference)!;
      expect(parseColorReference(text)).toEqual(reference);
      expect(serializeColorReference(parseColorReference(text)!)).toBe(text);
    });
  }

  it('writes the canonical spacing for a chain', () => {
    expect(serializeColorReference({
      variable: 'primary',
      modifiers: [{ op: 'darken', amount: 20 }, { op: 'opacity', amount: 70 }],
    })).toBe('{{ vars.primary | color | color_darken: 20 | color_opacity: 70 }}');
    expect(serializeColorReference({
      variable: 'primary',
      modifiers: [{ op: 'mix', amount: 50, mix: { kind: 'color', color: '#F00' } }, { op: 'mix', amount: 12.5, mix: { kind: 'variable', variable: 'accent' } }],
    })).toBe('{{ vars.primary | color | color_mix: "#ff0000", 50 | color_mix: vars.accent, 12.5 }}');
  });

  it('refuses to write a reference with more modifiers than a reader accepts', () => {
    const modifiers = Array.from({ length: 33 }, () => ({ op: 'hue' as const, amount: 1 }));
    expect(serializeColorReference({ variable: 'primary', modifiers })).toBeNull();
    expect(serializeColorReference({ variable: 'primary', modifiers: modifiers.slice(1) })).not.toBeNull();
  });

  it('resolves a chain from the current variable values and gives nothing for a missing variable', () => {
    const values: Record<string, string> = { primary: '#3366ff', white: '#ffffff' };
    const lookup = (name: string) => values[name];

    expect(resolveColorReference(parseColorReference('{{ vars.primary | color | color_darken: 20 | color_opacity: 70 }}')!, lookup))
      .toBe('#003df5b3');
    expect(resolveColorReference(parseColorReference('{{ vars.missing | color }}')!, lookup)).toBeNull();
    expect(resolveColorReference(parseColorReference('{{ vars.primary | color | color_mix: vars.missing, 50 }}')!, lookup)).toBeNull();
  });
});
