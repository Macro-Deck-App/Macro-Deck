import { Injectable } from '@angular/core';

import { shellBridge } from '../util/shell-bridge';

const BARE_MODIFIERS = new Set(['Control', 'Shift', 'Alt', 'Meta']);

export interface HotkeyCaptureHandlers {
  key: (event: KeyboardEvent) => void;
  cancel: () => void;
}

// Listening on the recorder button is not enough: WebKit follows native macOS behaviour and does not
// focus a <button> on click, so inside the desktop shell the keydown lands on the body and nothing
// is recorded (issue #423). Capturing on document also claims the key before anything else acts on
// it - a modal must not close on the Escape that cancels recording. While armed, the shell is asked
// to release its own key equivalents so combinations the application menu owns can be recorded too.
@Injectable({ providedIn: 'root' })
export class HotkeyCaptureService {
  private host: HTMLElement | null = null;
  private handlers: HotkeyCaptureHandlers | null = null;

  get active(): boolean {
    return this.handlers !== null;
  }

  start(host: HTMLElement, handlers: HotkeyCaptureHandlers): void {
    this.cancel();

    this.host = host;
    this.handlers = handlers;
    document.addEventListener('keydown', this.onKeyDown, true);
    document.addEventListener('mousedown', this.onPointerDown, true);
    window.addEventListener('blur', this.onWindowBlur);
    this.setShellCapture(true);
  }

  stop(): void {
    if (!this.handlers) {
      return;
    }

    this.host = null;
    this.handlers = null;
    document.removeEventListener('keydown', this.onKeyDown, true);
    document.removeEventListener('mousedown', this.onPointerDown, true);
    window.removeEventListener('blur', this.onWindowBlur);
    this.setShellCapture(false);
  }

  private cancel(): void {
    const handlers = this.handlers;
    this.stop();
    handlers?.cancel();
  }

  private readonly onKeyDown = (event: KeyboardEvent): void => {
    const handlers = this.handlers;
    if (!handlers) {
      return;
    }

    event.preventDefault();
    event.stopImmediatePropagation();

    if (BARE_MODIFIERS.has(event.key)) {
      return;
    }

    if (event.key === 'Escape') {
      this.cancel();
      return;
    }

    this.stop();
    handlers.key(event);
  };

  private readonly onPointerDown = (event: MouseEvent): void => {
    const target = event.target;
    if (target instanceof Node && this.host?.contains(target)) {
      return;
    }
    this.cancel();
  };

  private readonly onWindowBlur = (): void => {
    this.cancel();
  };

  private setShellCapture(active: boolean): void {
    void shellBridge()
      ?.setHotkeyCapture?.(active)
      .catch(() => undefined);
  }
}
