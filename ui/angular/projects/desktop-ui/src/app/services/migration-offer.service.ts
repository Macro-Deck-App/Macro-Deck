import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'md.migration.offerPending';

@Injectable({ providedIn: 'root' })
export class MigrationOfferService {
  readonly pending = signal(false);
  readonly visible = signal(false);

  constructor() {
    this.pending.set(this.readPending());
  }

  arm(): void {
    if (this.pending()) {
      return;
    }

    this.pending.set(true);
    try {
      localStorage.setItem(STORAGE_KEY, '1');
    } catch {
    }
  }

  present(): void {
    this.visible.set(true);
  }

  dismiss(): void {
    this.visible.set(false);
    if (!this.pending()) {
      return;
    }

    this.pending.set(false);
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
    }
  }

  private readPending(): boolean {
    try {
      return localStorage.getItem(STORAGE_KEY) === '1';
    } catch {
      return false;
    }
  }
}
