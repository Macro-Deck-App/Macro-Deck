import { Injectable, Signal, computed, effect, inject, signal, untracked } from '@angular/core';
import { GridRect, GridWidget, rectsOverlap } from '@macro-deck/runtime';
import { FolderService } from '@shared';

function widgetRect(widget: GridWidget): GridRect {
  return { x: widget.x, y: widget.y, w: widget.w, h: widget.h };
}

function boundingBox(a: GridRect, b: GridRect): GridRect {
  const x0 = Math.min(a.x, b.x);
  const y0 = Math.min(a.y, b.y);
  const x1 = Math.max(a.x + a.w, b.x + b.w);
  const y1 = Math.max(a.y + a.h, b.y + b.h);
  return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
}

@Injectable({ providedIn: 'root' })
export class WidgetSelectionService {
  private readonly folderService = inject(FolderService);

  private readonly _ids = signal<ReadonlySet<string>>(new Set());
  readonly ids: Signal<ReadonlySet<string>> = this._ids.asReadonly();
  readonly count = computed(() => this._ids().size);
  readonly hasSelection = computed(() => this._ids().size > 0);

  private anchorId: string | null = null;

  constructor() {
    effect(() => {
      this.folderService.selectedFolderId();
      this.clear();
    });

    effect(() => {
      const widgets = this.folderService.currentWidgets();
      const existing = untracked(this._ids);
      if (existing.size === 0) return;

      const present = new Set(widgets.map(w => w.id));
      const pruned = new Set([...existing].filter(id => present.has(id)));
      if (pruned.size !== existing.size) {
        this._ids.set(pruned);
      }
    });
  }

  has(id: string): boolean {
    return this._ids().has(id);
  }

  get anchorWidgetId(): string | null {
    return this.anchorId;
  }

  private isSelectable(widget: GridWidget): boolean {
    return widget.folderId === this.folderService.selectedFolderId();
  }

  private isSelectableId(id: string): boolean {
    const widget = this.folderService.currentWidgets().find(w => w.id === id);
    return widget !== undefined && this.isSelectable(widget);
  }

  selectOnly(id: string): void {
    if (!this.isSelectableId(id)) {
      this.clear();
      return;
    }
    this._ids.set(new Set([id]));
    this.anchorId = id;
  }

  toggle(id: string): void {
    const current = this._ids();
    if (!current.has(id) && !this.isSelectableId(id)) return;

    const next = new Set(current);
    if (!next.delete(id)) {
      next.add(id);
    }
    this._ids.set(next);
    this.anchorId = id;
  }

  deselect(id: string): void {
    const current = this._ids();
    if (!current.has(id)) return;

    const next = new Set(current);
    next.delete(id);
    this._ids.set(next);
  }

  selectRange(anchor: GridWidget, target: GridWidget, widgets: readonly GridWidget[]): void {
    const box = boundingBox(widgetRect(anchor), widgetRect(target));
    const ids = widgets
      .filter(widget => this.isSelectable(widget) && rectsOverlap(widgetRect(widget), box))
      .map(widget => widget.id);
    this._ids.set(new Set(ids));
  }

  setMany(ids: Iterable<string>, additive: boolean): void {
    const widgets = this.folderService.currentWidgets();
    const selectable = new Set(widgets.filter(widget => this.isSelectable(widget)).map(widget => widget.id));
    const incoming = new Set([...ids].filter(id => selectable.has(id)));

    if (!additive) {
      this._ids.set(incoming);
      return;
    }

    const next = new Set(this._ids());
    for (const id of incoming) {
      next.add(id);
    }
    this._ids.set(next);
  }

  selectAll(widgets: readonly GridWidget[]): void {
    this._ids.set(new Set(widgets.filter(widget => this.isSelectable(widget)).map(widget => widget.id)));
  }

  clear(): void {
    if (untracked(this._ids).size === 0 && this.anchorId === null) return;
    this._ids.set(new Set());
    this.anchorId = null;
  }
}
