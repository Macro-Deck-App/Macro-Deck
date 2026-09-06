import { AppStrings } from '@macro-deck/runtime';

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

export type TranslateFn = (key: string, args?: Record<string, unknown>) => string;

export function formatRelativeTime(date: Date | string | number, translate: TranslateFn): string {
  const then = date instanceof Date ? date.getTime() : new Date(date).getTime();
  if (Number.isNaN(then)) {
    return '';
  }

  const diffMs = Math.max(0, Date.now() - then);
  if (diffMs < MINUTE_MS) {
    return translate(AppStrings.Settings.RelativeTime.JustNow);
  }
  if (diffMs < HOUR_MS) {
    return translate(AppStrings.Settings.RelativeTime.MinutesAgo, { count: Math.round(diffMs / MINUTE_MS) });
  }
  if (diffMs < DAY_MS) {
    return translate(AppStrings.Settings.RelativeTime.HoursAgo, { count: Math.round(diffMs / HOUR_MS) });
  }
  return translate(AppStrings.Settings.RelativeTime.DaysAgo, { count: Math.round(diffMs / DAY_MS) });
}
