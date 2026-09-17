export const DOUBLE_TAP_WINDOW_MS = 400;

export const DOUBLE_TAP_DISTANCE_PX = 24;

export class TapSequencer {
  private pendingSingle: (() => void) | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private secondPress = false;
  private lastX: number | null = null;
  private lastY: number | null = null;

  pressStarted(x?: number, y?: number): void {
    if (this.pendingSingle === null) return;
    if (x !== undefined && y !== undefined && this.lastX !== null && this.lastY !== null
      && Math.hypot(x - this.lastX, y - this.lastY) > DOUBLE_TAP_DISTANCE_PX) {
      this.interrupted();
      return;
    }
    this.clearTimer();
    this.secondPress = true;
  }

  tapCompleted(doubleTapEnabled: boolean, single: () => void, double: () => void, x?: number, y?: number): void {
    if (this.secondPress && doubleTapEnabled) {
      this.pendingSingle = null;
      this.secondPress = false;
      double();
      return;
    }
    this.interrupted();
    if (!doubleTapEnabled) {
      single();
      return;
    }
    this.pendingSingle = single;
    this.lastX = x ?? null;
    this.lastY = y ?? null;
    this.timer = setTimeout(() => {
      this.timer = null;
      this.interrupted();
    }, DOUBLE_TAP_WINDOW_MS);
  }

  interrupted(): void {
    this.clearTimer();
    this.secondPress = false;
    const single = this.pendingSingle;
    this.pendingSingle = null;
    if (single !== null) single();
  }

  dispose(): void {
    this.clearTimer();
    this.secondPress = false;
    this.pendingSingle = null;
  }

  private clearTimer(): void {
    if (this.timer !== null) clearTimeout(this.timer);
    this.timer = null;
  }
}
