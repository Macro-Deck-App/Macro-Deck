import { Injectable, computed, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class PostUpdateChangelogService {
  private readonly pending = signal<ShellPostUpdateChangelog | null>(null);
  private loaded = false;

  readonly changelog = this.pending.asReadonly();
  readonly isOpen = computed(() => this.pending() !== null);

  async load(): Promise<void> {
    const bridge = window.macroDeckShell;
    if (this.loaded || typeof bridge?.getPostUpdateChangelog !== 'function') {
      return;
    }
    this.loaded = true;
    try {
      this.pending.set(await bridge.getPostUpdateChangelog());
    } catch {
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
