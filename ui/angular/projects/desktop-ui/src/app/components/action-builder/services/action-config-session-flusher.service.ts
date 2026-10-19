import { Injectable } from '@angular/core';

import type { UiSessionHandle } from '@shared';

export const ACTION_CONFIG_FLUSH_TIMEOUT_MS = 3000;

@Injectable({ providedIn: 'root' })
export class ActionConfigSessionFlusher {
  private readonly sessions = new Set<UiSessionHandle>();

  register(session: UiSessionHandle): () => void {
    this.sessions.add(session);
    return () => this.sessions.delete(session);
  }

  async flushAll(): Promise<void> {
    const pending = [...this.sessions].map(session => session.flush?.() ?? Promise.resolve());
    if (pending.length === 0) return;

    let timer: ReturnType<typeof setTimeout> | undefined;
    const timedOut = new Promise<boolean>(resolve => {
      timer = setTimeout(() => resolve(true), ACTION_CONFIG_FLUSH_TIMEOUT_MS);
    });
    const flushed = Promise.all(pending).then(() => false);

    const didTimeOut = await Promise.race([flushed, timedOut]);
    clearTimeout(timer);
    if (didTimeOut) {
      console.warn('Action configuration sessions did not confirm their reported values in time; saving anyway.');
    }
  }
}
