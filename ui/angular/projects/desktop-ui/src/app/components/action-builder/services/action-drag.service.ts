import { Injectable, NgZone, OnDestroy, inject, signal } from '@angular/core';

import type { ActionBlock } from '@macro-deck/runtime';
import { ActionFlowStore } from './action-flow.store';

export interface DropSlot {
  listId: string;
  index: number;
}

interface DragSession {
  block: ActionBlock;
  sourceListId: string;
  sourceIndex: number;
  wrapperEl: HTMLElement;
  startX: number;
  startY: number;
  active: boolean;
}

@Injectable()
export class ActionDragService implements OnDestroy {
  private static readonly DRAG_THRESHOLD = 5;
  private static readonly HIT_BUFFER = 8;
  private static readonly SCROLL_EDGE = 48;
  private static readonly SCROLL_SPEED = 9;

  private readonly store = inject(ActionFlowStore);
  private readonly zone = inject(NgZone);

  readonly dragging = signal<ActionBlock | null>(null);
  readonly activeSlot = signal<DropSlot | null>(null);
  readonly ghostX = signal(0);
  readonly ghostY = signal(0);
  readonly justDropped = signal<string | null>(null);

  private session: DragSession | null = null;
  private rootEl: HTMLElement | null = null;
  private scrollRaf: number | null = null;
  private droppedTimer: ReturnType<typeof setTimeout> | null = null;
  private lastPointer = { x: 0, y: 0 };
  private suppressNextClick = false;

  private readonly onMove = (e: PointerEvent) => this.handleMove(e);
  private readonly onUp = (e: PointerEvent) => this.handleUp(e);
  private readonly onKey = (e: KeyboardEvent) => {
    if (e.key === 'Escape') this.cancel();
  };

  attach(rootEl: HTMLElement): void {
    this.rootEl = rootEl;
  }

  isDragging(): boolean {
    return this.dragging() !== null;
  }

  isSource(blockId: string): boolean {
    return this.dragging()?.id === blockId;
  }

  consumeClickSuppression(): boolean {
    const suppressed = this.suppressNextClick;
    this.suppressNextClick = false;
    return suppressed;
  }

  press(event: PointerEvent, block: ActionBlock, originEl: HTMLElement): void {
    if (event.button !== 0 || this.session) return;
    const target = event.target as HTMLElement;
    if (target.closest('.action-header-button, input, select, textarea, a')) return;

    const wrapperEl = originEl.closest<HTMLElement>('.action-card-wrapper');
    const listEl = wrapperEl?.parentElement?.closest<HTMLElement>('.action-list');
    const listId = listEl?.dataset['listId'];
    if (!wrapperEl || !listEl || !listId) return;

    const sourceIndex = Array.prototype.indexOf.call(
      listEl.querySelectorAll(':scope > .action-card-wrapper'),
      wrapperEl,
    );
    if (sourceIndex < 0) return;

    this.session = {
      block,
      sourceListId: listId,
      sourceIndex,
      wrapperEl,
      startX: event.clientX,
      startY: event.clientY,
      active: false,
    };
    this.lastPointer = { x: event.clientX, y: event.clientY };

    this.zone.runOutsideAngular(() => {
      document.addEventListener('pointermove', this.onMove);
      document.addEventListener('pointerup', this.onUp);
      document.addEventListener('keydown', this.onKey);
    });
  }

  ngOnDestroy(): void {
    if (this.droppedTimer !== null) {
      clearTimeout(this.droppedTimer);
      this.droppedTimer = null;
    }
    this.teardown();
  }

  private handleMove(event: PointerEvent): void {
    const session = this.session;
    if (!session) return;
    this.lastPointer = { x: event.clientX, y: event.clientY };

    if (!session.active) {
      const dx = event.clientX - session.startX;
      const dy = event.clientY - session.startY;
      if (Math.hypot(dx, dy) < ActionDragService.DRAG_THRESHOLD) return;
      session.active = true;
      this.zone.run(() => {
        this.dragging.set(session.block);
        this.rootEl?.classList.add('ab-drag-active');
        this.startAutoScroll();
      });
    }

    event.preventDefault();
    this.zone.run(() => {
      this.ghostX.set(event.clientX);
      this.ghostY.set(event.clientY);
      this.activeSlot.set(this.hitTest(event.clientX, event.clientY));
    });
  }

  private handleUp(_event: PointerEvent): void {
    const session = this.session;
    if (!session) return;

    if (session.active) {
      this.suppressNextClick = true;
      const slot = this.activeSlot();
      this.zone.run(() => {
        if (slot) {
          this.store.moveBlock(session.sourceListId, session.sourceIndex, slot.listId, slot.index);
          this.flashDropped(session.block.id);
        }
      });
    }
    this.teardown();
  }

  private flashDropped(blockId: string): void {
    if (this.droppedTimer !== null) clearTimeout(this.droppedTimer);
    this.justDropped.set(blockId);
    this.droppedTimer = setTimeout(() => {
      this.zone.run(() => this.justDropped.set(null));
      this.droppedTimer = null;
    }, 550);
  }

  private cancel(): void {
    if (this.session?.active) this.suppressNextClick = true;
    this.teardown();
  }

  private teardown(): void {
    document.removeEventListener('pointermove', this.onMove);
    document.removeEventListener('pointerup', this.onUp);
    document.removeEventListener('keydown', this.onKey);
    if (this.scrollRaf !== null) {
      cancelAnimationFrame(this.scrollRaf);
      this.scrollRaf = null;
    }
    this.rootEl?.classList.remove('ab-drag-active');
    this.session = null;
    this.zone.run(() => {
      this.dragging.set(null);
      this.activeSlot.set(null);
    });
  }

  private hitTest(x: number, y: number): DropSlot | null {
    const root = this.rootEl;
    const session = this.session;
    if (!root || !session) return null;
    const buffer = ActionDragService.HIT_BUFFER;

    let bestList: HTMLElement | null = null;
    let bestDepth = -1;
    for (const list of root.querySelectorAll<HTMLElement>('.action-list')) {
      if (session.wrapperEl.contains(list)) continue;
      const r = list.getBoundingClientRect();
      if (
        x < r.left - buffer || x > r.right + buffer ||
        y < r.top - buffer || y > r.bottom + buffer
      ) {
        continue;
      }
      let depth = 0;
      for (let el = list.parentElement; el && el !== root; el = el.parentElement) depth++;
      if (depth > bestDepth) {
        bestDepth = depth;
        bestList = list;
      }
    }
    if (!bestList) return null;

    const listId = bestList.dataset['listId'];
    if (!listId) return null;

    const wrappers = bestList.querySelectorAll<HTMLElement>(':scope > .action-card-wrapper');
    let index = wrappers.length;
    for (let i = 0; i < wrappers.length; i++) {
      const r = wrappers[i].getBoundingClientRect();
      if (y < r.top + r.height / 2) {
        index = i;
        break;
      }
    }

    if (
      listId === session.sourceListId &&
      (index === session.sourceIndex || index === session.sourceIndex + 1)
    ) {
      return null;
    }

    return { listId, index };
  }

  private startAutoScroll(): void {
    const panel = this.rootEl?.querySelector<HTMLElement>('.action-panel');
    if (!panel) return;
    const step = () => {
      if (!this.session?.active) return;
      const rect = panel.getBoundingClientRect();
      const { y } = this.lastPointer;
      if (y < rect.top + ActionDragService.SCROLL_EDGE && panel.scrollTop > 0) {
        panel.scrollTop -= ActionDragService.SCROLL_SPEED;
      } else if (y > rect.bottom - ActionDragService.SCROLL_EDGE) {
        panel.scrollTop += ActionDragService.SCROLL_SPEED;
      }
      this.scrollRaf = requestAnimationFrame(step);
    };
    this.zone.runOutsideAngular(() => {
      this.scrollRaf = requestAnimationFrame(step);
    });
  }
}
