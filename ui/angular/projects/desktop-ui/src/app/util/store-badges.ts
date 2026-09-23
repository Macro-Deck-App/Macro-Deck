import { StoreCatalogItemBody } from '@macro-deck/runtime';

const DAY_MS = 24 * 60 * 60 * 1000;
const NEW_WINDOW_MS = 30 * DAY_MS;
const UPDATED_WINDOW_MS = 14 * DAY_MS;

export type StoreFreshness = 'new' | 'updated';

function time(value: string | null | undefined): number | null {
  if (!value) {
    return null;
  }
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}

export function storeFreshness(item: Pick<StoreCatalogItemBody, 'createdAt' | 'updatedAt'>,
  now: number = Date.now()): StoreFreshness | null {
  const created = time(item.createdAt);
  if (created !== null && created <= now && now - created <= NEW_WINDOW_MS) {
    return 'new';
  }
  const updated = time(item.updatedAt);
  if (updated !== null && updated <= now && now - updated <= UPDATED_WINDOW_MS
    && (created === null || updated - created > DAY_MS)) {
    return 'updated';
  }
  return null;
}
