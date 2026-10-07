export interface RgbaColor {
  r: number;
  g: number;
  b: number;
  a: number;
}

export type ColorModifierOp =
  | 'lighten'
  | 'darken'
  | 'opacity'
  | 'increase_opacity'
  | 'reduce_opacity'
  | 'saturate'
  | 'desaturate'
  | 'hue'
  | 'mix';

export const COLOR_MODIFIER_OPS: readonly ColorModifierOp[] = [
  'lighten',
  'darken',
  'opacity',
  'increase_opacity',
  'reduce_opacity',
  'saturate',
  'desaturate',
  'hue',
  'mix',
];

const SHORT_HEX = /^#([0-9a-f])([0-9a-f])([0-9a-f])([0-9a-f])?$/;
const LONG_HEX = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})?$/;
const RGB_FUNCTION = /^rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)$/;
const RGBA_FUNCTION = /^rgba\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d*\.?\d+)\s*\)$/;

export function parseColor(input: string | null | undefined): RgbaColor | null {
  if (typeof input !== 'string') return null;
  const text = input.trim().toLowerCase();

  const short = SHORT_HEX.exec(text);
  if (short) {
    return {
      r: parseInt(short[1] + short[1], 16),
      g: parseInt(short[2] + short[2], 16),
      b: parseInt(short[3] + short[3], 16),
      a: short[4] !== undefined ? parseInt(short[4] + short[4], 16) : 255,
    };
  }

  const long = LONG_HEX.exec(text);
  if (long) {
    return {
      r: parseInt(long[1], 16),
      g: parseInt(long[2], 16),
      b: parseInt(long[3], 16),
      a: long[4] !== undefined ? parseInt(long[4], 16) : 255,
    };
  }

  const rgb = RGB_FUNCTION.exec(text) ?? RGBA_FUNCTION.exec(text);
  if (rgb) {
    const channels = [Number(rgb[1]), Number(rgb[2]), Number(rgb[3])];
    if (channels.some(channel => channel > 255)) return null;
    let alpha = 255;
    if (rgb[4] !== undefined) {
      const unit = Number(rgb[4]);
      if (!(unit >= 0 && unit <= 1)) return null;
      alpha = toByte(unit * 255);
    }
    return { r: channels[0], g: channels[1], b: channels[2], a: alpha };
  }

  return null;
}

export function formatColor(color: RgbaColor): string {
  const hex = '#' + byteHex(color.r) + byteHex(color.g) + byteHex(color.b);
  return color.a === 255 ? hex : hex + byteHex(color.a);
}

export function canonicalColor(input: string | null | undefined): string | null {
  const color = parseColor(input);
  return color ? formatColor(color) : null;
}

export function isOpaqueColor(color: RgbaColor): boolean {
  return color.a === 255;
}

export function applyColorModifier(color: RgbaColor, op: ColorModifierOp, amount: number, mixWith?: RgbaColor): RgbaColor {
  switch (op) {
    case 'lighten':
      return adjustHls(color, (h, l, s) => [h, l + (1 - l) * percent(amount), s]);
    case 'darken':
      return adjustHls(color, (h, l, s) => [h, l * (1 - percent(amount)), s]);
    case 'saturate':
      return adjustHls(color, (h, l, s) => [h, l, s + (1 - s) * percent(amount)]);
    case 'desaturate':
      return adjustHls(color, (h, l, s) => [h, l, s * (1 - percent(amount))]);
    case 'hue':
      return adjustHls(color, (h, l, s) => [positiveModulo(h * 360 + amount, 360) / 360, l, s]);
    case 'opacity':
      return { r: color.r, g: color.g, b: color.b, a: toByte(percent(amount) * 255) };
    case 'increase_opacity': {
      const alpha = color.a / 255;
      return { r: color.r, g: color.g, b: color.b, a: toByte((alpha + (1 - alpha) * percent(amount)) * 255) };
    }
    case 'reduce_opacity':
      return { r: color.r, g: color.g, b: color.b, a: toByte((color.a / 255) * (1 - percent(amount)) * 255) };
    case 'mix': {
      if (!mixWith) return color;
      const p = percent(amount);
      const blend = (from: number, to: number) => toByte(from + (to - from) * p);
      return {
        r: blend(color.r, mixWith.r),
        g: blend(color.g, mixWith.g),
        b: blend(color.b, mixWith.b),
        a: blend(color.a, mixWith.a),
      };
    }
    default:
      return color;
  }
}

function adjustHls(
  color: RgbaColor,
  adjust: (h: number, l: number, s: number) => [number, number, number],
): RgbaColor {
  const [h, l, s] = rgbToHls(color.r / 255, color.g / 255, color.b / 255);
  const [nh, nl, ns] = adjust(h, l, s);
  const [r, g, b] = hlsToRgb(nh, nl, ns);
  return { r: toByte(r * 255), g: toByte(g * 255), b: toByte(b * 255), a: color.a };
}

// Mirrors Python colorsys on doubles, which generated the shared vectors in ui-model/fixtures/colors.
function rgbToHls(r: number, g: number, b: number): [number, number, number] {
  const maxc = Math.max(r, g, b);
  const minc = Math.min(r, g, b);
  const sumc = maxc + minc;
  const rangec = maxc - minc;
  const l = sumc / 2;
  if (minc === maxc) return [0, l, 0];
  const s = l <= 0.5 ? rangec / sumc : rangec / (2 - maxc - minc);
  const rc = (maxc - r) / rangec;
  const gc = (maxc - g) / rangec;
  const bc = (maxc - b) / rangec;
  let h: number;
  if (r === maxc) h = bc - gc;
  else if (g === maxc) h = 2 + rc - bc;
  else h = 4 + gc - rc;
  return [positiveModulo(h / 6, 1), l, s];
}

function hlsToRgb(h: number, l: number, s: number): [number, number, number] {
  if (s === 0) return [l, l, l];
  const m2 = l <= 0.5 ? l * (1 + s) : l + s - l * s;
  const m1 = 2 * l - m2;
  return [hueChannel(m1, m2, h + 1 / 3), hueChannel(m1, m2, h), hueChannel(m1, m2, h - 1 / 3)];
}

function hueChannel(m1: number, m2: number, hue: number): number {
  const h = positiveModulo(hue, 1);
  if (h < 1 / 6) return m1 + (m2 - m1) * h * 6;
  if (h < 0.5) return m2;
  if (h < 2 / 3) return m1 + (m2 - m1) * (2 / 3 - h) * 6;
  return m1;
}

function positiveModulo(value: number, modulus: number): number {
  return value - Math.floor(value / modulus) * modulus;
}

function percent(amount: number): number {
  if (!Number.isFinite(amount)) return 0;
  return Math.min(Math.max(amount, 0), 100) / 100;
}

function toByte(value: number): number {
  return Math.min(Math.max(Math.floor(value + 0.5), 0), 255);
}

function byteHex(value: number): string {
  return ('0' + value.toString(16)).slice(-2);
}
