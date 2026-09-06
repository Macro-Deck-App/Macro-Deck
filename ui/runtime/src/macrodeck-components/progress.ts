import { hasIntlParts, pad2 } from '../ui-framework/intl-support';
import { UiNode } from '../ui-framework/ui-node.interface';
import { nodeRaw } from '../ui-framework/node-properties.util';

export interface UiProgressRef {
  positionMs: number;
  anchorMs: number;
  durationMs?: number;
  rate: number;
}

export const UiProgressFormats = {
  Elapsed: 'elapsed',
  Remaining: 'remaining',
  Duration: 'duration',
} as const;

export const UI_PROGRESS_FORMATS_WELL_KNOWN: readonly string[] = [
  UiProgressFormats.Elapsed, UiProgressFormats.Remaining, UiProgressFormats.Duration,
];

const PROGRESS_MARKER = '$progress';

export function nodeProgressRef(node: UiNode | null | undefined, key: string): UiProgressRef | undefined {
  const raw = nodeRaw(node, key);
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return undefined;

  const marker = (raw as Record<string, unknown>)[PROGRESS_MARKER];
  if (typeof marker !== 'object' || marker === null || Array.isArray(marker)) return undefined;

  const { positionMs, durationMs, rate, anchor } = marker as Record<string, unknown>;
  if (typeof positionMs !== 'number' || typeof anchor !== 'string') return undefined;

  const anchorMs = Date.parse(anchor);
  if (Number.isNaN(anchorMs)) return undefined;

  return {
    positionMs,
    anchorMs,
    durationMs: typeof durationMs === 'number' && durationMs > 0 ? durationMs : undefined,
    rate: typeof rate === 'number' ? rate : 1,
  };
}

export function resolveProgressMs(reference: UiProgressRef, nowMs: number): number {
  const advanced = reference.positionMs + (nowMs - reference.anchorMs) * reference.rate;
  const floored = Math.max(0, advanced);
  return reference.durationMs === undefined ? floored : Math.min(reference.durationMs, floored);
}

export function resolveProgressFraction(reference: UiProgressRef, nowMs: number): number {
  if (reference.durationMs === undefined) return 0;
  return Math.min(1, Math.max(0, resolveProgressMs(reference, nowMs) / reference.durationMs));
}

export function formatDuration(totalMs: number, locale: string): string {
  const totalSeconds = Math.max(0, Math.floor(totalMs / 1000));
  const seconds = totalSeconds % 60;
  const minutes = Math.floor(totalSeconds / 60) % 60;
  const hours = Math.floor(totalSeconds / 3600);

  // `toLocaleString` exists without `Intl`, but silently ignores the options with it - which is how
  // a duration came out as "3:5" rather than "03:05" on the compatibility floor.
  const digits = (value: number, pad: number) =>
    (hasIntlParts()
      ? value.toLocaleString(locale, { minimumIntegerDigits: pad, useGrouping: false })
      : (pad > 1 ? pad2(value) : String(value)));

  return hours > 0
    ? `${digits(hours, 1)}:${digits(minutes, 2)}:${digits(seconds, 2)}`
    : `${digits(minutes, 1)}:${digits(seconds, 2)}`;
}

export function formatProgressRun(
  reference: UiProgressRef,
  format: string | undefined,
  nowMs: number,
  locale: string,
): string {
  switch (format) {
    case UiProgressFormats.Elapsed:
      return formatDuration(resolveProgressMs(reference, nowMs), locale);
    case UiProgressFormats.Remaining:
      return reference.durationMs === undefined
        ? ''
        : formatDuration(reference.durationMs - resolveProgressMs(reference, nowMs), locale);
    case UiProgressFormats.Duration:
      return reference.durationMs === undefined ? '' : formatDuration(reference.durationMs, locale);
    default:
      return '';
  }
}
