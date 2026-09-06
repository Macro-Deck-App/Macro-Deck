import { Injectable, signal } from '@angular/core';

import { ShellCursorPosition, shellBridge } from '../util/shell-bridge';

export const SCREEN_CURSOR_POLL_MS = 60;

@Injectable({ providedIn: 'root' })
export class ScreenCursorService {
  readonly supported = typeof shellBridge()?.getCursorPosition === 'function';

  private readonly currentPosition = signal<ShellCursorPosition | null>(null);

  readonly position = this.currentPosition.asReadonly();

  private trackers = 0;
  private timer: ReturnType<typeof setInterval> | null = null;
  private sampling = false;

  track(): () => void {
    if (!this.supported) {
      return () => undefined;
    }

    this.trackers++;
    this.start();

    let released = false;
    return () => {
      if (released) {
        return;
      }
      released = true;
      this.trackers--;
      if (this.trackers === 0) {
        this.stop();
      }
    };
  }

  async sample(): Promise<void> {
    const bridge = shellBridge();
    if (!bridge?.getCursorPosition || this.sampling || document.hidden) {
      return;
    }

    this.sampling = true;
    try {
      this.apply(await bridge.getCursorPosition());
    } catch {
      // The bridge call itself failing is not transient (a missing IPC grant,
      // not a cursor the platform cannot locate), so stop rather than retry it
      // every tick for as long as the readout stays mounted.
      this.currentPosition.set(null);
      this.stopTimer();
    } finally {
      this.sampling = false;
    }
  }

  private apply(next: ShellCursorPosition | null): void {
    const current = this.currentPosition();
    if (!next) {
      if (current) {
        this.currentPosition.set(null);
      }
      return;
    }
    // Only a real move is a signal write: a resting cursor must not re-render
    // the readout 16 times a second.
    if (!current || current.x !== next.x || current.y !== next.y) {
      this.currentPosition.set(next);
    }
  }

  private start(): void {
    if (this.timer !== null) {
      return;
    }
    this.timer = setInterval(() => void this.sample(), SCREEN_CURSOR_POLL_MS);
    void this.sample();
  }

  private stop(): void {
    this.stopTimer();
    this.currentPosition.set(null);
  }

  private stopTimer(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
