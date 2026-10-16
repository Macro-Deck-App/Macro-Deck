import { InjectionToken } from '@angular/core';

export const STORE_SHARE_ORIGIN = 'https://store.macro-deck.app';

export const STORE_SHARE_LINKS_ENABLED = new InjectionToken<boolean>('STORE_SHARE_LINKS_ENABLED', {
  providedIn: 'root',
  factory: () => true,
});

export function storeShareUrl(packageId: string): string {
  return `${STORE_SHARE_ORIGIN}/${encodeURIComponent(packageId)}`;
}
