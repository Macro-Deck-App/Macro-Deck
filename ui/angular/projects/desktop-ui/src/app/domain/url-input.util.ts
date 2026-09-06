import { containsLiquid } from '@macro-deck/runtime';

const EXPLICIT_PROTOCOL_PATTERN = /^(?:[a-z][a-z\d+.-]*:\/\/|https?:\/)/i;
const HTTP_URL_PREFIX_PATTERN = /^https?:\/\//i;

export function normalizeHttpsUrl(value: string): string {
  const trimmed = value.trim();
  if (!trimmed || containsLiquid(trimmed) || EXPLICIT_PROTOCOL_PATTERN.test(trimmed)) {
    return trimmed;
  }

  return trimmed.startsWith('//') ? `https:${trimmed}` : `https://${trimmed}`;
}

export function isValidUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return (url.protocol !== 'http:' && url.protocol !== 'https:') || HTTP_URL_PREFIX_PATTERN.test(value);
  } catch {
    return false;
  }
}
