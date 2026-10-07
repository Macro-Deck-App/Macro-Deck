import { applyColorModifier, type ColorModifierOp, formatColor, parseColor, type RgbaColor } from './color';

export type ColorMixTarget = { kind: 'color'; color: string } | { kind: 'variable'; variable: string };

export interface ColorModifier {
  op: ColorModifierOp;
  amount: number;
  mix?: ColorMixTarget;
}

export interface ColorReference {
  variable: string;
  modifiers: ColorModifier[];
}

const NAME = '[a-z][a-z0-9_]*';
const NUMBER = '-?\\d+(?:\\.\\d+)?';
const MIX_LITERAL = '#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3})';
const HEAD = new RegExp(`^\\{\\{\\s*vars\\.(${NAME})\\s*\\|\\s*color\\s*`);
const AMOUNT_STEP = new RegExp(`^\\|\\s*color_(lighten|darken|saturate|desaturate|increase_opacity|reduce_opacity|opacity|hue)\\s*:\\s*(${NUMBER})\\s*`);
const MIX_STEP = new RegExp(`^\\|\\s*color_mix\\s*:\\s*(?:vars\\.(${NAME})|"(${MIX_LITERAL})")\\s*,\\s*(${NUMBER})\\s*`);
const TAIL = /^\}\}$/;
const VARIABLE_NAME = new RegExp(`^${NAME}$`);

export const MAX_COLOR_REFERENCE_LENGTH = 1024;
export const MAX_COLOR_MODIFIERS = 32;

export function parseColorReference(text: string | null | undefined): ColorReference | null {
  if (typeof text !== 'string') return null;
  let rest = text.trim();
  if (rest.length > MAX_COLOR_REFERENCE_LENGTH) return null;
  const head = HEAD.exec(rest);
  if (!head) return null;
  rest = rest.slice(head[0].length);

  const modifiers: ColorModifier[] = [];
  for (;;) {
    if (TAIL.test(rest)) return { variable: head[1], modifiers };
    if (modifiers.length >= MAX_COLOR_MODIFIERS) return null;

    const amountStep = AMOUNT_STEP.exec(rest);
    if (amountStep) {
      modifiers.push({ op: amountStep[1] as ColorModifierOp, amount: Number(amountStep[2]) });
      rest = rest.slice(amountStep[0].length);
      continue;
    }

    const mixStep = MIX_STEP.exec(rest);
    if (mixStep) {
      const mix: ColorMixTarget = mixStep[1] !== undefined
        ? { kind: 'variable', variable: mixStep[1] }
        : { kind: 'color', color: mixStep[2] };
      modifiers.push({ op: 'mix', amount: Number(mixStep[3]), mix });
      rest = rest.slice(mixStep[0].length);
      continue;
    }

    return null;
  }
}

export function isColorReference(text: string | null | undefined): boolean {
  return parseColorReference(text) !== null;
}

// A template that is not a plain color is Liquid the picker cannot edit; it must not be overwritten silently.
export function isUnsupportedColorTemplate(text: string | null | undefined): boolean {
  return typeof text === 'string' && /\{\{|\{%/.test(text) && !isColorReference(text);
}

export function serializeColorReference(reference: ColorReference): string | null {
  if (!VARIABLE_NAME.test(reference.variable) || reference.modifiers.length > MAX_COLOR_MODIFIERS) return null;
  let text = `{{ vars.${reference.variable} | color`;
  for (const modifier of reference.modifiers) {
    const amount = formatAmount(modifier.amount);
    if (modifier.op === 'mix') {
      const target = mixTargetText(modifier.mix);
      if (target === null) return null;
      text += ` | color_mix: ${target}, ${amount}`;
    } else {
      text += ` | color_${modifier.op}: ${amount}`;
    }
  }
  text = `${text} }}`;
  return text.length > MAX_COLOR_REFERENCE_LENGTH ? null : text;
}

export type ColorVariableLookup = (name: string) => string | null | undefined;

export function resolveColorReference(reference: ColorReference, lookup: ColorVariableLookup): string | null {
  let color = parseColor(lookup(reference.variable) ?? null);
  if (!color) return null;
  for (const modifier of reference.modifiers) {
    let mixWith: RgbaColor | null | undefined;
    if (modifier.op === 'mix') {
      mixWith = modifier.mix?.kind === 'variable'
        ? parseColor(lookup(modifier.mix.variable) ?? null)
        : parseColor(modifier.mix?.color ?? null);
      if (!mixWith) return null;
    }
    color = applyColorModifier(color, modifier.op, modifier.amount, mixWith ?? undefined);
  }
  return formatColor(color);
}

function mixTargetText(target: ColorMixTarget | undefined): string | null {
  if (!target) return null;
  if (target.kind === 'variable') return VARIABLE_NAME.test(target.variable) ? `vars.${target.variable}` : null;
  const color = parseColor(target.color);
  return color ? `"${formatColor(color)}"` : null;
}

function formatAmount(amount: number): string {
  if (!Number.isFinite(amount)) return '0';
  const rounded = Math.round(amount * 1000) / 1000;
  return String(rounded === 0 ? 0 : rounded);
}
