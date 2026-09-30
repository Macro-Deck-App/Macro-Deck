import { InjectionToken } from '@angular/core';

export const STORE_SHARE_ORIGIN = 'https://store.macro-deck.app';

// Off until the Store website that answers these addresses is live.
export const STORE_SHARE_LINKS_ENABLED = new InjectionToken<boolean>('STORE_SHARE_LINKS_ENABLED', {
  providedIn: 'root',
  factory: () => false,
});

export function storeShareUrl(packageId: string): string {
  return `${STORE_SHARE_ORIGIN}/${encodeURIComponent(packageId)}`;
}
