import { Injectable } from '@angular/core';
import { Navigation } from '@angular/router';
import { StoreExtensionKind } from '@macro-deck/runtime';

export type StoreBrowseView = 'discover' | 'installed';

export const STORE_DETAIL_KINDS: readonly StoreExtensionKind[] = ['Plugin', 'IconPack', 'ProfileTemplate'];

const STORE_DETAIL_URL = new RegExp(`^/store/(${STORE_DETAIL_KINDS.join('|')})/`);
const STORE_LIST_URL = /^\/store(\/installed|\/tests)?(\?|$)/;

export function isStoreDetailUrl(url: string | null | undefined): boolean {
  return STORE_DETAIL_URL.test(url ?? '');
}

export function isStoreListUrl(url: string | null | undefined): boolean {
  return STORE_LIST_URL.test(url ?? '');
}

export function isHistoryNavigation(navigation: Navigation | null | undefined): boolean {
  return navigation?.trigger === 'popstate' || navigation?.trigger === 'hashchange';
}

export interface StoreBrowseSnapshot {
  key: string;
  scrollTop: number;
  itemCount: number;
}

@Injectable({ providedIn: 'root' })
export class StoreBrowseStateService {
  private readonly snapshots = new Map<StoreBrowseView, StoreBrowseSnapshot>();

  save(view: StoreBrowseView, snapshot: StoreBrowseSnapshot): void {
    this.snapshots.set(view, snapshot);
  }

  forget(view: StoreBrowseView): void {
    this.snapshots.delete(view);
  }

  restore(view: StoreBrowseView, key: string): StoreBrowseSnapshot | null {
    const snapshot = this.snapshots.get(view) ?? null;
    this.snapshots.delete(view);
    return snapshot?.key === key ? snapshot : null;
  }
}
