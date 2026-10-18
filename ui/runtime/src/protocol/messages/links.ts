export const UI_CLIENT_FEATURE_OPEN_URL = 'open-url';

export interface UiOpenUrlEvent {
  url: string;
}

export function isOpenableWebUrl(value: unknown): value is string {
  if (typeof value !== 'string') return false;
  try {
    const parsed = new URL(value);
    return (parsed.protocol === 'http:' || parsed.protocol === 'https:') && parsed.host.length > 0;
  } catch {
    return false;
  }
}
