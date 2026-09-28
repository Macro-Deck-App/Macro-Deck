import { Injectable, computed, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class PostUpdateChangelogService {
  private readonly pending = signal<ShellPostUpdateChangelog | null>(null);
  private readonly _settled = signal(false);
  private loaded = false;

  readonly changelog = this.pending.asReadonly();
  readonly isOpen = computed(() => this.pending() !== null);
  readonly settled = this._settled.asReadonly();

  async load(): Promise<void> {
    const bridge = window.macroDeckShell;
    if (this.loaded) {
      return;
    }
    if (typeof bridge?.getPostUpdateChangelog !== 'function') {
      this._settled.set(true);
      return;
    }
    this.loaded = true;
    try {
      this.pending.set(await bridge.getPostUpdateChangelog());
    } catch {
    } finally {
      this._settled.set(true);
    }
  }

  dismiss(): void {
    if (this.pending() === null) {
      return;
    }
    this.pending.set(null);
    const bridge = window.macroDeckShell;
    if (typeof bridge?.dismissPostUpdateChangelog === 'function') {
      bridge.dismissPostUpdateChangelog().catch(() => {
      });
    }
  }
}
