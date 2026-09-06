import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../transport/api.service';
import { GetKeyRingStatusResponse, TransportError, UnlockKeyRingResponse } from '@macro-deck/runtime';

@Injectable({ providedIn: 'root' })
export class KeyRingService {
  private readonly api = inject(ApiService);

  readonly status = signal<GetKeyRingStatusResponse | null>(null);
  readonly locked = signal(false);

  async probe(): Promise<void> {
    try {
      // Mirrors AuthService.bootstrap(): this runs before anything else has resolved the host's base
      // URL, so without this call every request would go out relative to the UI's own origin instead
      // of the host's - usually the same origin in production, but not in general, and not in a test
      // environment that stubs HOST_URL_RESOLVER to simulate "not reachable yet".
      const baseUrl = await this.api.resolveBaseUrl();
      if (!baseUrl) {
        return;
      }

      const status = await this.api.getKeyRingStatus();
      this.status.set(status);
      this.locked.set(status.locked);
    } catch (error) {
      // A host that answered - an older build with no such route, or an outright failure - has told us
      // what it knows, and it is not locked. Only an unreachable host is worth waiting for; treating a
      // definitive answer as "keep retrying" would strand the app on the splash screen forever.
      if (error instanceof TransportError) {
        this.status.set({ locked: false, lockReason: 'None', restartSupported: false });
        this.locked.set(false);
        return;
      }

      console.warn('Failed to probe the key ring status (the host may not be reachable yet):', error);
    }
  }

  unlock(recoveryKey: string): Promise<UnlockKeyRingResponse> {
    return this.api.unlockKeyRing({ recoveryKey });
  }
}
