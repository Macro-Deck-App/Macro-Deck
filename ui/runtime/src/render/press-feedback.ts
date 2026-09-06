export const PRESS_FEEDBACK_MIN_VISIBLE_MS = 60;

export class PressFeedback {
  private pressed = false;
  private pressedAt = 0;
  private releaseTimeout: ReturnType<typeof setTimeout> | null = null;

  constructor(private readonly onChange: (pressed: boolean) => void) {}

  isPressed(): boolean {
    return this.pressed;
  }

  press(): void {
    this.clearReleaseTimeout();
    this.pressedAt = Date.now();
    this.setPressed(true);
  }

  release(): void {
    if (!this.pressed) return;

    const remaining = PRESS_FEEDBACK_MIN_VISIBLE_MS - (Date.now() - this.pressedAt);
    if (remaining <= 0) {
      this.setPressed(false);
      return;
    }

    this.releaseTimeout = setTimeout(() => {
      this.releaseTimeout = null;
      this.setPressed(false);
    }, remaining);
  }

  dispose(): void {
    this.clearReleaseTimeout();
    this.setPressed(false);
  }

  private setPressed(pressed: boolean): void {
    if (this.pressed === pressed) return;
    this.pressed = pressed;
    this.onChange(pressed);
  }

  private clearReleaseTimeout(): void {
    if (this.releaseTimeout !== null) {
      clearTimeout(this.releaseTimeout);
      this.releaseTimeout = null;
    }
  }
}
