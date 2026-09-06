import { Injectable, signal } from '@angular/core';

export type RenderingMode = 'standard' | 'simple';

export const DEFAULT_RENDERING_MODE: RenderingMode = 'standard';

export const SIMPLE_RENDERING_CLASS = 'md-simple-rendering';

const STORAGE_KEY = 'macro-deck.rendering-mode';

@Injectable({ providedIn: 'root' })
export class RenderingModeService {
  private readonly _mode = signal<RenderingMode>(DEFAULT_RENDERING_MODE);

  readonly mode = this._mode.asReadonly();

  constructor() {
    this.setMode(this.readPreference());
  }

  setMode(mode: RenderingMode): void {
    this._mode.set(mode);
    this.applyToDom(mode);

    try {
      localStorage.setItem(STORAGE_KEY, mode);
    } catch {
      // A device with storage disabled still renders; it just forgets the choice on reload.
    }
  }

  private applyToDom(mode: RenderingMode): void {
    const root = document.documentElement;
    if (mode === 'simple') {
      root.classList.add(SIMPLE_RENDERING_CLASS);
    } else {
      root.classList.remove(SIMPLE_RENDERING_CLASS);
    }
  }

  private readPreference(): RenderingMode {
    try {
      return localStorage.getItem(STORAGE_KEY) === 'simple' ? 'simple' : DEFAULT_RENDERING_MODE;
    } catch {
      return DEFAULT_RENDERING_MODE;
    }
  }
}
