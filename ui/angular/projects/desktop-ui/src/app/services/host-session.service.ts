import { Injectable, inject } from '@angular/core';
import { ApiService } from '@shared';

const STORAGE_KEY = 'macro-deck.host-session';

@Injectable({ providedIn: 'root' })
export class HostSessionService {
  private readonly api = inject(ApiService);

  private checking = false;

  start(): void {
    this.api.connectionState$.subscribe(state => {
      if (state === 'connected') {
        void this.check();
      }
    });
  }

  private async check(): Promise<void> {
    if (this.checking) {
      return;
    }
    this.checking = true;

    try {
      const session = await this.api.getHostSession();
      const previous = this.read();

      if (previous === null) {
        this.write(session.sessionId);
        return;
      }

      if (previous === session.sessionId) {
        return;
      }

      // Written before reloading, so the fresh page recognises this host instead of reloading again.
      this.write(session.sessionId);
      this.reload();
    } catch {
    } finally {
      this.checking = false;
    }
  }

  private read(): string | null {
    try {
      return sessionStorage.getItem(STORAGE_KEY);
    } catch {
      return null;
    }
  }

  private write(sessionId: string): void {
    try {
      sessionStorage.setItem(STORAGE_KEY, sessionId);
    } catch {
    }
  }

  protected reload(): void {
    window.location.reload();
  }
}
