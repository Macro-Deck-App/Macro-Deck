import { Injectable, computed, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class WidgetStyleClipboardService {
  private readonly _style = signal<string | null>(null);

  readonly style = this._style.asReadonly();

  readonly hasContent = computed(() => this._style() !== null);

  set(style: string): void {
    this._style.set(style);
  }
}
