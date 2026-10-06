export interface ThresholdBand {
  id: string;
  color: string;
  from?: number;
}

export interface ThresholdsValue {
  bands: ThresholdBand[];
}

export interface ThresholdAxis {
  start: number;
  end: number;
}

export const MAX_THRESHOLD_BANDS = 64;

const COLOR_PATTERN = /^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/;

// Same rules as UiThresholds in ui-model; both are checked against ui-model/fixtures/thresholds.
export function readThresholds(raw: unknown): ThresholdsValue | null {
  if (!isRecord(raw) || !Array.isArray(raw['bands'])) return null;
  const source = raw['bands'] as unknown[];
  if (source.length === 0 || source.length > MAX_THRESHOLD_BANDS) return null;

  const ids = new Set<string>();
  const bands: ThresholdBand[] = [];
  let previous: number | undefined;

  for (let i = 0; i < source.length; i++) {
    const item = source[i];
    if (!isRecord(item)) return null;
    const { id, color, from } = item;
    if (typeof id !== 'string' || id.length === 0 || ids.has(id)) return null;
    if (typeof color !== 'string' || !COLOR_PATTERN.test(color)) return null;
    if (from !== undefined && from !== null && typeof from !== 'number') return null;

    const start = typeof from === 'number' ? from : undefined;
    if (i === 0 && start !== undefined) return null;
    if (i > 0 && (start === undefined || !Number.isFinite(start) || (previous !== undefined && start <= previous))) {
      return null;
    }

    ids.add(id);
    previous = start;
    const band: ThresholdBand = { id, color: normalizeColor(color) };
    if (start !== undefined) band.from = start;
    bands.push(band);
  }

  return { bands };
}

export function thresholdBandIndexAt(thresholds: ThresholdsValue, value: number): number {
  if (Number.isNaN(value)) return -1;
  let index = 0;
  for (let i = 1; i < thresholds.bands.length; i++) {
    if (value >= (thresholds.bands[i].from as number)) index = i;
  }
  return index;
}

export function thresholdBandAt(thresholds: ThresholdsValue, value: number): ThresholdBand | null {
  const index = thresholdBandIndexAt(thresholds, value);
  return index < 0 ? null : thresholds.bands[index];
}

export function thresholdAxis(thresholds: ThresholdsValue, min: number, max: number): ThresholdAxis {
  let start = Math.min(min, max);
  let end = Math.max(min, max);
  for (const band of thresholds.bands) {
    if (band.from === undefined) continue;
    start = Math.min(start, band.from);
    end = Math.max(end, band.from);
  }
  return { start, end };
}

export function thresholdBandRange(
  thresholds: ThresholdsValue,
  index: number,
  axis: ThresholdAxis,
): { from: number; to: number } {
  const band = thresholds.bands[index];
  const next = thresholds.bands[index + 1];
  return { from: band.from ?? axis.start, to: next?.from ?? axis.end };
}

export function moveThresholdBoundary(
  thresholds: ThresholdsValue,
  index: number,
  to: number,
  gap: number,
): ThresholdsValue {
  if (index < 1 || index >= thresholds.bands.length || !Number.isFinite(to)) return thresholds;
  const previous = index > 1 ? (thresholds.bands[index - 1].from as number) + gap : -Infinity;
  const next = index < thresholds.bands.length - 1 ? (thresholds.bands[index + 1].from as number) - gap : Infinity;
  if (previous > next) return thresholds;
  const clamped = Math.min(Math.max(to, previous), next);
  if (clamped === thresholds.bands[index].from) return thresholds;
  return {
    bands: thresholds.bands.map((band, i) => (i === index ? { ...band, from: clamped } : band)),
  };
}

export function insertThresholdBoundary(
  thresholds: ThresholdsValue,
  at: number,
  gap: number,
  band: { id: string; color: string },
  maxCount?: number | null,
): ThresholdsValue | null {
  const limit = Math.min(maxCount ?? MAX_THRESHOLD_BANDS, MAX_THRESHOLD_BANDS);
  if (!Number.isFinite(at) || thresholds.bands.length >= limit) return null;
  if (thresholds.bands.some(existing => existing.id === band.id)) return null;
  const index = thresholdBandIndexAt(thresholds, at);
  const from = thresholds.bands[index].from;
  const next = thresholds.bands[index + 1]?.from;
  if ((from !== undefined && at - from < gap) || (next !== undefined && next - at < gap)) return null;
  const bands = [...thresholds.bands];
  bands.splice(index + 1, 0, { id: band.id, color: normalizeColor(band.color), from: at });
  return { bands };
}

export function removeThresholdBand(thresholds: ThresholdsValue, index: number): ThresholdsValue | null {
  if (thresholds.bands.length <= 1 || index < 0 || index >= thresholds.bands.length) return null;
  const bands = thresholds.bands.filter((_, i) => i !== index);
  if (index === 0) {
    const { from: _dropped, ...first } = bands[0];
    bands[0] = first;
  }
  return { bands };
}

export function recolorThresholdBand(thresholds: ThresholdsValue, index: number, color: string): ThresholdsValue {
  if (!COLOR_PATTERN.test(color)) return thresholds;
  return {
    bands: thresholds.bands.map((band, i) => (i === index ? { ...band, color: normalizeColor(color) } : band)),
  };
}

export function snapThresholdValue(value: number, step: number | null | undefined, origin: number): number {
  if (!step || step <= 0 || !Number.isFinite(step)) return value;
  const snapped = origin + Math.round((value - origin) / step) * step;
  return Number(snapped.toFixed(thresholdStepDecimals(step)));
}

function normalizeColor(color: string): string {
  const hex = color.slice(1).toLowerCase();
  return hex.length === 3 ? `#${hex[0]}${hex[0]}${hex[1]}${hex[1]}${hex[2]}${hex[2]}` : `#${hex}`;
}

export function thresholdStepDecimals(step: number): number {
  if (!Number.isFinite(step) || step <= 0) return 0;
  const [mantissa, exponent] = String(step).split('e');
  const dot = mantissa.indexOf('.');
  const decimals = (dot < 0 ? 0 : mantissa.length - dot - 1) - Number(exponent ?? 0);
  return Math.min(Math.max(decimals, 0), 20);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
