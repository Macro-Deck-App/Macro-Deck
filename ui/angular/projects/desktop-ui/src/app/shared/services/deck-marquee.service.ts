import { Injectable, NgZone, OnDestroy, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';

import { CellDimensions, clampRectToGrid, GridRect } from '@macro-deck/runtime';
import { DECK_DRAG_THRESHOLD_PX } from './deck-drag.service';

export interface DeckMarqueeConfig {
  gridEl: HTMLElement;
  cell: CellDimensions;
  cols: number;
  rows: number;
}

interface DeckMarqueeSession {
  config: DeckMarqueeConfig;
  pointerId: number;
  startX: number;
  startY: number;
  gridLeft: number;
  gridTop: number;
  active: boolean;
}

export interface MarqueeBox {
  left: number;
  top: number;
  width: number;
  height: number;
}

@Injectable()
export class DeckMarqueeService implements OnDestroy {
  private readonly zone = inject(NgZone);

  readonly box = signal<MarqueeBox | null>(null);
  readonly completed = new Subject<{ rect: GridRect; additive: boolean }>();

  private session: DeckMarqueeSession | null = null;
  private overlayEl: HTMLElement | null = null;
  private rafId: number | null = null;
  private lastPointer = { x: 0, y: 0 };
  private suppressNextClick = false;

  private readonly onMove = (e: PointerEvent) => this.handleMove(e);
  private readonly onUp = (e: PointerEvent) => this.handleUp(e);
  private readonly onCancel = () => this.cancel();
  private readonly onKey = (e: KeyboardEvent) => {
    if (e.key === 'Escape') this.cancel();
  };

  attachOverlay(el: HTMLElement): void {
    this.overlayEl = el;
  }

  detachOverlay(el: HTMLElement): void {
    if (this.overlayEl === el) {
      this.overlayEl = null;
    }
  }

  consumeClickSuppression(): boolean {
    const suppressed = this.suppressNextClick;
    this.suppressNextClick = false;
    return suppressed;
  }

  isActive(): boolean {
    return this.session !== null;
  }

  press(event: PointerEvent, config: DeckMarqueeConfig): void {
    if (event.button !== 0 || this.session) return;

    this.suppressNextClick = false;

    const gridRect = config.gridEl.getBoundingClientRect();
    this.session = {
      config,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      gridLeft: gridRect.left,
      gridTop: gridRect.top,
      active: false,
    };
    this.lastPointer = { x: event.clientX, y: event.clientY };

    this.zone.runOutsideAngular(() => {
      document.addEventListener('pointermove', this.onMove);
      document.addEventListener('pointerup', this.onUp);
      document.addEventListener('pointercancel', this.onCancel);
      document.addEventListener('keydown', this.onKey);
    });
  }

  ngOnDestroy(): void {
    this.cancel();
  }

  private handleMove(event: PointerEvent): void {
    const session = this.session;
    if (!session) return;
    this.lastPointer = { x: event.clientX, y: event.clientY };

    if (!session.active) {
      const dx = event.clientX - session.startX;
      const dy = event.clientY - session.startY;
      if (Math.hypot(dx, dy) < DECK_DRAG_THRESHOLD_PX) return;
      this.activate(session);
    }

    event.preventDefault();
    this.scheduleFrame();
  }

  private activate(session: DeckMarqueeSession): void {
    session.active = true;
    this.zone.run(() => {
      this.box.set(this.computeBoxPx(session));
    });
  }

  private scheduleFrame(): void {
    if (this.rafId !== null) return;
    this.rafId = requestAnimationFrame(() => {
      this.rafId = null;
      this.onFrame();
    });
  }

  private onFrame(): void {
    const session = this.session;
    if (!session?.active) return;

    const box = this.computeBoxPx(session);
    if (this.overlayEl) {
      this.overlayEl.style.left = `${box.left}px`;
      this.overlayEl.style.top = `${box.top}px`;
      this.overlayEl.style.width = `${box.width}px`;
      this.overlayEl.style.height = `${box.height}px`;
    }
  }

  private computeBoxPx(session: DeckMarqueeSession): MarqueeBox {
    const startX = session.startX - session.gridLeft;
    const startY = session.startY - session.gridTop;
    const currentX = this.lastPointer.x - session.gridLeft;
    const currentY = this.lastPointer.y - session.gridTop;
    return {
      left: Math.min(startX, currentX),
      top: Math.min(startY, currentY),
      width: Math.abs(currentX - startX),
      height: Math.abs(currentY - startY),
    };
  }

  private boxToGridRect(session: DeckMarqueeSession, box: MarqueeBox): GridRect {
    const { cell, cols, rows } = session.config;
    const xRange = DeckMarqueeService.axisRange(box.left, box.left + box.width, cell.padding, cell.cellWidth, cell.gap, cols);
    const yRange = DeckMarqueeService.axisRange(box.top, box.top + box.height, cell.padding, cell.cellHeight, cell.gap, rows);
    return clampRectToGrid(
      { x: xRange.start, y: yRange.start, w: xRange.length, h: yRange.length },
      cols,
      rows,
    );
  }

  private static axisRange(
    startPx: number,
    endPx: number,
    padding: number,
    size: number,
    gap: number,
    count: number,
  ): { start: number; length: number } {
    const stride = size + gap;
    if (stride <= 0 || count <= 0) {
      return { start: 0, length: Math.max(1, count) };
    }

    const clampIndex = (value: number) => Math.max(0, Math.min(count - 1, value));
    const startIndex = clampIndex(Math.floor((startPx - padding) / stride));
    const endIndex = clampIndex(Math.floor((endPx - padding) / stride));
    return { start: startIndex, length: endIndex - startIndex + 1 };
  }

  private handleUp(event: PointerEvent): void {
    const session = this.session;
    if (!session) return;
    this.lastPointer = { x: event.clientX, y: event.clientY };

    if (!session.active) {
      this.stopListening();
      this.session = null;
      return;
    }

    this.suppressNextClick = true;
    const box = this.computeBoxPx(session);
    const rect = this.boxToGridRect(session, box);
    const additive = event.ctrlKey || event.metaKey;

    this.stopListening();
    this.session = null;
    this.zone.run(() => {
      this.box.set(null);
      this.completed.next({ rect, additive });
    });
  }

  private cancel(): void {
    const session = this.session;
    if (!session) return;
    if (session.active) {
      this.suppressNextClick = true;
    }
    this.stopListening();
    this.session = null;
    this.zone.run(() => this.box.set(null));
  }

  private stopListening(): void {
    document.removeEventListener('pointermove', this.onMove);
    document.removeEventListener('pointerup', this.onUp);
    document.removeEventListener('pointercancel', this.onCancel);
    document.removeEventListener('keydown', this.onKey);
    if (this.rafId !== null) {
      cancelAnimationFrame(this.rafId);
      this.rafId = null;
    }
  }
}
