import { Injectable, signal } from '@angular/core';

const STORAGE_PREFIX = 'md.hint.dismissed.';

@Injectable({ providedIn: 'root' })
export class DismissibleHintService {
  private readonly dismissed = signal<ReadonlySet<string>>(new Set());

  constructor() {
    this.dismissed.set(new Set(this.readDismissedKeys()));
  }

  isDismissed(key: string): boolean {
    return this.dismissed().has(key);
  }

  dismiss(key: string): void {
    if (this.isDismissed(key)) {
      return;
    }

    this.dismissed.update(keys => new Set(keys).add(key));
    try {
      localStorage.setItem(STORAGE_PREFIX + key, '1');
    } catch {
    }
  }

  private readDismissedKeys(): string[] {
    try {
      return Object.keys(localStorage)
        .filter(key => key.startsWith(STORAGE_PREFIX))
        .map(key => key.slice(STORAGE_PREFIX.length));
    } catch {
      return [];
    }
  }
}
