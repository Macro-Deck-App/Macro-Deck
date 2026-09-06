export interface Hsv {
  h: number;
  s: number;
  v: number;
}

const HEX_PATTERN = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i;

export function normalizeHex(value: string): string | null {
  const match = HEX_PATTERN.exec(value.trim());
  if (!match) {
    return null;
  }

  const digits = match[1];
  const expanded = digits.length === 3
    ? digits.split('').map(digit => digit + digit).join('')
    : digits;

  return `#${expanded.toLowerCase()}`;
}

export function hexToHsv(hex: string): Hsv | null {
  const normalized = normalizeHex(hex);
  if (!normalized) {
    return null;
  }

  const r = parseInt(normalized.slice(1, 3), 16) / 255;
  const g = parseInt(normalized.slice(3, 5), 16) / 255;
  const b = parseInt(normalized.slice(5, 7), 16) / 255;

  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const delta = max - min;

  let h = 0;
  if (delta !== 0) {
    if (max === r) {
      h = ((g - b) / delta) % 6;
    } else if (max === g) {
      h = (b - r) / delta + 2;
    } else {
      h = (r - g) / delta + 4;
    }

    h *= 60;
    if (h < 0) {
      h += 360;
    }
  }

  return {
    h,
    s: max === 0 ? 0 : delta / max,
    v: max,
  };
}

export function hsvToHex({ h, s, v }: Hsv): string {
  const chroma = v * s;
  const sector = (((h % 360) + 360) % 360) / 60;
  const x = chroma * (1 - Math.abs((sector % 2) - 1));
  const m = v - chroma;

  const [r, g, b] = sectorChannels(sector, chroma, x);

  return `#${[r, g, b].map(channel => toHexByte(channel + m)).join('')}`;
}

function sectorChannels(sector: number, chroma: number, x: number): [number, number, number] {
  if (sector < 1) {
    return [chroma, x, 0];
  }
  if (sector < 2) {
    return [x, chroma, 0];
  }
  if (sector < 3) {
    return [0, chroma, x];
  }
  if (sector < 4) {
    return [0, x, chroma];
  }
  if (sector < 5) {
    return [x, 0, chroma];
  }

  return [chroma, 0, x];
}

function toHexByte(channel: number): string {
  return Math.round(Math.min(1, Math.max(0, channel)) * 255).toString(16).padStart(2, '0');
}
