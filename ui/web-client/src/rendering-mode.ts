export type RenderingMode = 'standard' | 'simple';

export const DEFAULT_RENDERING_MODE: RenderingMode = 'standard';

export const SIMPLE_RENDERING_CLASS = 'md-simple-rendering';

const STORAGE_KEY = 'macro-deck.rendering-mode';

export class RenderingModeStore {
  private mode: RenderingMode;
  private readonly listeners: Array<() => void> = [];

  constructor() {
    this.mode = RenderingModeStore.readPreference();
    this.applyToDom();
  }

  get(): RenderingMode {
    return this.mode;
  }

  simple(): boolean {
    return this.mode === 'simple';
  }

  onChange(listener: () => void): () => void {
    this.listeners.push(listener);
    return () => {
      const at = this.listeners.indexOf(listener);
      if (at >= 0) this.listeners.splice(at, 1);
    };
  }

  set(mode: RenderingMode): void {
    if (mode === this.mode) return;
    this.mode = mode;
    this.applyToDom();

    try {
      window.localStorage.setItem(STORAGE_KEY, mode);
    } catch {
      // A device with storage disabled still renders; it just forgets the choice on reload.
    }

    for (let index = 0; index < this.listeners.length; index++) this.listeners[index]();
  }

  private applyToDom(): void {
    const root = document.documentElement;
    if (this.mode === 'simple') root.classList.add(SIMPLE_RENDERING_CLASS);
    else root.classList.remove(SIMPLE_RENDERING_CLASS);
  }

  private static readPreference(): RenderingMode {
    try {
      return window.localStorage.getItem(STORAGE_KEY) === 'simple' ? 'simple' : DEFAULT_RENDERING_MODE;
    } catch {
      return DEFAULT_RENDERING_MODE;
    }
  }
}
