const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'touchstart', 'keydown', 'wheel'];

export interface IdleTimerClock {
  set(callback: () => void, delayMs: number): unknown;
  clear(handle: unknown): void;
}

const REAL_CLOCK: IdleTimerClock = {
  set: (callback, delayMs) => setTimeout(callback, delayMs),
  clear: handle => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

export class IdleTimer {
  private delayMs: number | null = null;
  private handle: unknown = null;
  private readonly teardowns: Array<() => void> = [];

  constructor(
    private readonly onIdle: () => void,
    private readonly target: EventTarget = document,
    private readonly clock: IdleTimerClock = REAL_CLOCK,
  ) {}

  configure(delayMs: number | null): void {
    const next = delayMs !== null && delayMs > 0 ? delayMs : null;
    // An unchanged setting keeps the running countdown: re-arming on every unrelated change would
    // push the screensaver out each time the connection or the screen so much as flickers.
    if (next === this.delayMs && (next === null || this.handle !== null)) return;
    this.delayMs = next;
    this.stop();
    if (this.delayMs === null) return;

    for (let index = 0; index < ACTIVITY_EVENTS.length; index++) {
      const type = ACTIVITY_EVENTS[index];
      const listener = () => this.restart();
      // Capture phase: a press a widget node claims stops propagating in the bubble phase, and a tap on
      // such a node is still activity.
      this.target.addEventListener(type, listener, { capture: true, passive: true });
      this.teardowns.push(() => this.target.removeEventListener(type, listener, { capture: true }));
    }
    this.restart();
  }

  restart(): void {
    if (this.delayMs === null) return;
    this.clearHandle();
    this.handle = this.clock.set(() => {
      this.handle = null;
      this.onIdle();
    }, this.delayMs);
  }

  stop(): void {
    this.clearHandle();
    for (let index = 0; index < this.teardowns.length; index++) this.teardowns[index]();
    this.teardowns.length = 0;
  }

  running(): boolean {
    return this.handle !== null;
  }

  private clearHandle(): void {
    if (this.handle === null) return;
    this.clock.clear(this.handle);
    this.handle = null;
  }
}
