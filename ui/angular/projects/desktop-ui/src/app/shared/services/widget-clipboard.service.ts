import { Injectable, computed, signal } from '@angular/core';

import { GridWidget, WidgetClipboardEntry, WidgetData, WidgetType } from '@macro-deck/runtime';

@Injectable({ providedIn: 'root' })
export class WidgetClipboardService {
  private readonly _entries = signal<readonly WidgetClipboardEntry[]>([]);
  private readonly _originAnchor = signal<{ x: number; y: number } | null>(null);

  readonly entries = this._entries.asReadonly();

  readonly originAnchor = this._originAnchor.asReadonly();

  readonly hasContent = computed(() => this._entries().length > 0);

  readonly cutWidgetIds = computed<ReadonlySet<string>>(() => {
    const ids = this._entries()
      .filter(entry => entry.isCut && entry.origin)
      .map(entry => entry.origin!.widgetId);
    return new Set(ids);
  });

  copy(widget: GridWidget, folderId: string): void {
    this.copyMany([widget], folderId);
  }

  cut(widget: GridWidget, folderId: string): void {
    this.cutMany([widget], folderId);
  }

  copyMany(widgets: readonly GridWidget[], folderId: string): void {
    this.setEntries(widgets, false, folderId);
  }

  cutMany(widgets: readonly GridWidget[], folderId: string): void {
    this.setEntries(widgets, true, folderId);
  }

  clear(): void {
    this._entries.set([]);
    this._originAnchor.set(null);
  }

  private setEntries(widgets: readonly GridWidget[], isCut: boolean, folderId: string | null): void {
    if (widgets.length === 0) {
      this._entries.set([]);
      this._originAnchor.set(null);
      return;
    }

    const minX = Math.min(...widgets.map(w => w.x));
    const minY = Math.min(...widgets.map(w => w.y));
    this._entries.set(this.snapshot(widgets, minX, minY, isCut, folderId));
    this._originAnchor.set({ x: minX, y: minY });
  }

  private snapshot(
    widgets: readonly GridWidget[],
    minX: number,
    minY: number,
    isCut: boolean,
    folderId: string | null,
  ): WidgetClipboardEntry[] {
    return widgets.map(widget => ({
      type: widget.type,
      w: widget.w,
      h: widget.h,
      data: JSON.parse(JSON.stringify(widget.data)) as WidgetData,
      dx: widget.x - minX,
      dy: widget.y - minY,
      isCut,
      origin: folderId ? { widgetId: widget.id, folderId } : null,
    }));
  }
}
