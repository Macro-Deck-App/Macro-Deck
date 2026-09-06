import { Injectable, effect, inject, signal } from '@angular/core';

import { ApiService } from '../transport';

@Injectable({ providedIn: 'root' })
export class ServerClockService {
  private readonly api = inject(ApiService, { optional: true });

  private readonly offsetMs = signal(0);

  private readonly revision = signal(0);

  private syncing = false;

  readonly offset = this.offsetMs.asReadonly();

  constructor() {
    const api = this.api;
    if (api) {
      effect(() => {
        if (api.connectionStateSignal() === 'connected') {
          void this.sync();
        }
      });
    }

    document.addEventListener('visibilitychange', () => {
      if (!document.hidden) {
        this.revision.update(value => value + 1);
        void this.sync();
      }
    });
  }

  now(): number {
    this.revision();

    return Date.now() + this.offsetMs();
  }

  async sync(): Promise<void> {
    if (this.syncing || !this.api) {
      return;
    }

    this.syncing = true;
    try {
      const sent = Date.now();
      const response = await this.api.getServerTime();
      const received = Date.now();
      this.offsetMs.set(Math.round(response.utcMs + (received - sent) / 2 - received));
    } catch {
    } finally {
      this.syncing = false;
    }
  }
}
