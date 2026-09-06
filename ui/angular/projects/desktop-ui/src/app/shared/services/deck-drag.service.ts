import { Injectable, NgZone, OnDestroy, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';

import {
  CellDimensions,
  GridPlacement,
  GridRect,
  GridWidget,
  rectsEqual,
  reflowLayout,
  snapDrag,
  snapGroupDelta,
  snapResize,
  validateGroupMove,
} from '@macro-deck/runtime';

export type DeckDragMode = 'DRAG' | 'RESIZE';

export const DECK_DRAG_THRESHOLD_PX = 5;

export interface DeckDragConfig {
  widgets: readonly GridWidget[];
  cols: number;
  rows: number;
  cell: CellDimensions;
  gridEl: HTMLElement;
  sourceEl: HTMLElement;
}

interface DeckDragSession {
  widget: GridWidget;
  mode: DeckDragMode;
  initialRect: GridRect;
  originalLayout: readonly GridPlacement[];
  cols: number;
  rows: number;
  cell: CellDimensions;
  gridEl: HTMLElement;
  sourceEl: HTMLElement;
  pointerId: number;
  startX: number;
  startY: number;
  grabOffsetX: number;
  grabOffsetY: number;
  active: boolean;
  members: readonly GridWidget[] | null;
  memberIds: ReadonlySet<string> | null;
  groupBounds: GridRect | null;
}

function groupBoundingBox(widgets: readonly GridWidget[]): GridRect {
  const xs0 = widgets.map(w => w.x);
  const ys0 = widgets.map(w => w.y);
  const xs1 = widgets.map(w => w.x + w.w);
  const ys1 = widgets.map(w => w.y + w.h);
  const x = Math.min(...xs0);
  const y = Math.min(...ys0);
  return { x, y, w: Math.max(...xs1) - x, h: Math.max(...ys1) - y };
}

@Injectable()
export class DeckDragService implements OnDestroy {
  private static readonly _settleMs = 140;
  private static readonly _justDroppedMs = 250;

  private readonly zone = inject(NgZone);

  readonly ghostWidgets = signal<readonly { widget: GridWidget; offsetX: number; offsetY: number }[]>([]);
  readonly session = signal<{ widgetId: string; mode: DeckDragMode; memberIds: ReadonlySet<string> } | null>(null);

  isActive(): boolean {
    return this.session() !== null;
  }
  readonly placeholders = signal<readonly GridRect[]>([]);
  readonly displacements = signal<ReadonlyMap<string, GridRect>>(new Map());
  readonly justDropped = signal<ReadonlySet<string>>(new Set());
  readonly settlingGhost = signal(false);
  readonly dropped = new Subject<ReadonlyMap<string, GridRect>>();

  private sessionState: DeckDragSession | null = null;
  private ghostEl: HTMLElement | null = null;
  private ghostTransform = '';
  private rafId: number | null = null;
  private lastPointer = { x: 0, y: 0 };
  private lastCandidateKey = '';
  private lastValid: { candidate: GridRect; placements: ReadonlyMap<string, GridRect> } | null = null;
  private suppressNextClick = false;
  private settleTimer: ReturnType<typeof setTimeout> | null = null;
  private droppedTimer: ReturnType<typeof setTimeout> | null = null;
  private dragActiveCleanupTimer: ReturnType<typeof setTimeout> | null = null;

  private readonly onMove = (e: PointerEvent) => this.handleMove(e);
  private readonly onUp = () => this.handleUp();
  private readonly onCancel = () => this.cancel();
  private readonly onKey = (e: KeyboardEvent) => {
    if (e.key === 'Escape') this.cancel();
  };

  isDragSource(widgetId: string): boolean {
    const session = this.session();
    return session !== null && session.mode === 'DRAG' && session.memberIds.has(widgetId);
  }

  consumeClickSuppression(): boolean {
    const suppressed = this.suppressNextClick;
    this.suppressNextClick = false;
    return suppressed;
  }

  press(event: PointerEvent, widget: GridWidget, mode: DeckDragMode, config: DeckDragConfig): void {
    if (event.button !== 0 || this.sessionState) return;

    this.suppressNextClick = false;

    const sourceRect = config.sourceEl.getBoundingClientRect();
    this.sessionState = {
      widget,
      mode,
      initialRect: { x: widget.x, y: widget.y, w: widget.w, h: widget.h },
      // Pinned widgets are locked: reflow never displaces them and a candidate that overlaps
      // one is rejected, so a drag can neither cover nor push a pinned widget (issue #117).
      originalLayout: config.widgets.map(w => ({ id: w.id, x: w.x, y: w.y, w: w.w, h: w.h, locked: w.isPinned })),
      cols: config.cols,
      rows: config.rows,
      cell: config.cell,
      gridEl: config.gridEl,
      sourceEl: config.sourceEl,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      grabOffsetX: event.clientX - sourceRect.left,
      grabOffsetY: event.clientY - sourceRect.top,
      active: false,
      members: null,
      memberIds: null,
      groupBounds: null,
    };
    this.lastPointer = { x: event.clientX, y: event.clientY };

    this.zone.runOutsideAngular(() => {
      document.addEventListener('pointermove', this.onMove);
      document.addEventListener('pointerup', this.onUp);
      document.addEventListener('pointercancel', this.onCancel);
      document.addEventListener('keydown', this.onKey);
    });
  }

  pressGroup(event: PointerEvent, primary: GridWidget, members: readonly GridWidget[], config: DeckDragConfig): void {
    if (event.button !== 0 || this.sessionState) return;

    this.suppressNextClick = false;

    const sourceRect = config.sourceEl.getBoundingClientRect();
    const memberIds = new Set(members.map(w => w.id));
    this.sessionState = {
      widget: primary,
      mode: 'DRAG',
      initialRect: { x: primary.x, y: primary.y, w: primary.w, h: primary.h },
      originalLayout: config.widgets.map(w => ({ id: w.id, x: w.x, y: w.y, w: w.w, h: w.h, locked: w.isPinned })),
      cols: config.cols,
      rows: config.rows,
      cell: config.cell,
      gridEl: config.gridEl,
      sourceEl: config.sourceEl,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      grabOffsetX: event.clientX - sourceRect.left,
      grabOffsetY: event.clientY - sourceRect.top,
      active: false,
      members,
      memberIds,
      groupBounds: groupBoundingBox(members),
    };
    this.lastPointer = { x: event.clientX, y: event.clientY };

    this.zone.runOutsideAngular(() => {
      document.addEventListener('pointermove', this.onMove);
      document.addEventListener('pointerup', this.onUp);
      document.addEventListener('pointercancel', this.onCancel);
      document.addEventListener('keydown', this.onKey);
    });
  }

  attachGhost(el: HTMLElement): void {
    this.ghostEl = el;
    if (this.ghostTransform) {
      el.style.transform = this.ghostTransform;
    }
  }

  detachGhost(el: HTMLElement): void {
    if (this.ghostEl === el) {
      this.ghostEl = null;
    }
  }

  ngOnDestroy(): void {
    if (this.droppedTimer !== null) {
      clearTimeout(this.droppedTimer);
      this.droppedTimer = null;
    }
    this.cancel();
    if (this.dragActiveCleanupTimer !== null) {
      clearTimeout(this.dragActiveCleanupTimer);
      this.dragActiveCleanupTimer = null;
    }
  }

  private handleMove(event: PointerEvent): void {
    const session = this.sessionState;
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

  private activate(session: DeckDragSession): void {
    session.active = true;
    try {
      session.sourceEl.setPointerCapture(session.pointerId);
    } catch {
    }
    const sourceRect = session.sourceEl.getBoundingClientRect();
    this.ghostTransform = `translate3d(${sourceRect.left}px, ${sourceRect.top}px, 0)`;
    if (this.dragActiveCleanupTimer !== null) {
      clearTimeout(this.dragActiveCleanupTimer);
      this.dragActiveCleanupTimer = null;
    }
    session.gridEl.classList.add('drag-active');

    this.zone.run(() => {
      const memberIds = session.memberIds ?? new Set([session.widget.id]);
      this.session.set({ widgetId: session.widget.id, mode: session.mode, memberIds });
      if (session.mode === 'DRAG') {
        this.ghostWidgets.set(
          session.members
            ? session.members.map(w => ({
                widget: w,
                offsetX: (w.x - session.widget.x) * (session.cell.cellWidth + session.cell.gap),
                offsetY: (w.y - session.widget.y) * (session.cell.cellHeight + session.cell.gap),
              }))
            : [{ widget: session.widget, offsetX: 0, offsetY: 0 }],
        );
      }
      this.lastValid = {
        candidate: session.initialRect,
        placements: new Map(session.originalLayout.map(w => [w.id, { x: w.x, y: w.y, w: w.w, h: w.h }])),
      };
      this.lastCandidateKey = session.members
        ? DeckDragService.groupCandidateKey(0, 0)
        : DeckDragService.candidateKey(session.initialRect);
      this.placeholders.set(
        session.members
          ? session.members.map(w => ({ x: w.x, y: w.y, w: w.w, h: w.h }))
          : [session.initialRect],
      );
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
    const session = this.sessionState;
    if (!session?.active) return;

    if (session.mode === 'DRAG') {
      const x = this.lastPointer.x - session.grabOffsetX;
      const y = this.lastPointer.y - session.grabOffsetY;
      this.ghostTransform = `translate3d(${x}px, ${y}px, 0)`;
      if (this.ghostEl) {
        this.ghostEl.style.transform = this.ghostTransform;
      }
    }

    const dxPx = this.lastPointer.x - session.startX;
    const dyPx = this.lastPointer.y - session.startY;

    if (session.members && session.memberIds) {
      this.onGroupFrame(session, session.members, session.memberIds, dxPx, dyPx);
      return;
    }

    const candidate = session.mode === 'DRAG'
      ? snapDrag(session.initialRect, dxPx, dyPx, session.cell, session.cols, session.rows)
      : snapResize(session.initialRect, dxPx, dyPx, session.cell, session.cols, session.rows);

    const key = DeckDragService.candidateKey(candidate);
    if (key === this.lastCandidateKey) return;
    this.lastCandidateKey = key;

    const result = reflowLayout(session.originalLayout, session.widget.id, candidate, session.cols, session.rows);
    if (!result.ok) {
      return;
    }

    this.zone.run(() => {
      this.lastValid = { candidate, placements: result.placements };
      this.placeholders.set([candidate]);
      this.displacements.set(this.buildDisplacements(session, result.placements));
    });
  }

  private onGroupFrame(
    session: DeckDragSession,
    members: readonly GridWidget[],
    memberIds: ReadonlySet<string>,
    dxPx: number,
    dyPx: number,
  ): void {
    const { dx, dy } = snapGroupDelta(session.groupBounds!, dxPx, dyPx, session.cell, session.cols, session.rows);

    const key = DeckDragService.groupCandidateKey(dx, dy);
    if (key === this.lastCandidateKey) return;
    this.lastCandidateKey = key;

    const placements = validateGroupMove(session.originalLayout, memberIds, dx, dy, session.cols, session.rows);
    if (!placements) {
      return;
    }

    const primaryRect = placements.get(session.widget.id) ?? session.initialRect;
    this.zone.run(() => {
      this.lastValid = { candidate: primaryRect, placements };
      this.placeholders.set(members.map(w => placements.get(w.id)!));
      this.displacements.set(new Map());
    });
  }

  private buildDisplacements(
    session: DeckDragSession,
    placements: ReadonlyMap<string, GridRect>,
  ): ReadonlyMap<string, GridRect> {
    const map = new Map<string, GridRect>();
    for (const original of session.originalLayout) {
      if (original.id === session.widget.id && session.mode === 'DRAG') continue;
      const target = placements.get(original.id);
      if (target && !rectsEqual(target, original)) {
        map.set(original.id, target);
      }
    }
    return map;
  }

  private handleUp(): void {
    const session = this.sessionState;
    if (!session) return;

    if (!session.active) {
      this.finish(null);
      return;
    }

    this.suppressNextClick = true;
    this.onFrame();
    const lastValid = this.lastValid;
    const changes = new Map<string, GridRect>();
    if (lastValid) {
      for (const original of session.originalLayout) {
        const target = lastValid.placements.get(original.id);
        if (target && !rectsEqual(target, original)) {
          changes.set(original.id, target);
        }
      }
    }

    const landedIds = session.memberIds ? [...session.memberIds] : [session.widget.id];

    this.stopListening();
    this.zone.run(() => {
      this.placeholders.set([]);
      this.displacements.set(new Map());
      if (changes.size > 0) {
        this.dropped.next(changes);
      }
    });

    if (session.mode === 'DRAG' && this.ghostEl && lastValid && !DeckDragService.prefersReducedMotion()) {
      this.settleGhost(session, lastValid.candidate, landedIds);
    } else {
      this.finish(landedIds);
    }
  }

  private settleGhost(session: DeckDragSession, target: GridRect, landedIds: readonly string[]): void {
    const ghostEl = this.ghostEl;
    if (!ghostEl) {
      this.finish(landedIds);
      return;
    }

    const gridRect = session.gridEl.getBoundingClientRect();
    const { cell } = session;
    const x = gridRect.left + cell.padding + target.x * (cell.cellWidth + cell.gap);
    const y = gridRect.top + cell.padding + target.y * (cell.cellHeight + cell.gap);

    this.settlingGhost.set(true);
    ghostEl.style.transform = `translate3d(${x}px, ${y}px, 0)`;

    this.settleTimer = setTimeout(() => {
      this.settleTimer = null;
      this.finish(landedIds);
    }, DeckDragService._settleMs + 20);
  }

  private cancel(): void {
    const session = this.sessionState;
    if (!session) return;
    if (session.active) {
      this.suppressNextClick = true;
    }
    this.stopListening();
    this.finish(null);
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
    if (this.settleTimer !== null) {
      clearTimeout(this.settleTimer);
      this.settleTimer = null;
    }
  }

  private finish(landedIds: readonly string[] | null): void {
    this.stopListening();
    const session = this.sessionState;
    const gridEl = session?.gridEl ?? null;
    if (gridEl) {
      this.dragActiveCleanupTimer = setTimeout(() => {
        this.dragActiveCleanupTimer = null;
        gridEl.classList.remove('drag-active');
      }, 180);
    }
    this.sessionState = null;
    this.lastValid = null;
    this.lastCandidateKey = '';
    this.ghostTransform = '';

    this.zone.run(() => {
      this.session.set(null);
      this.ghostWidgets.set([]);
      this.placeholders.set([]);
      this.displacements.set(new Map());
      this.settlingGhost.set(false);
      if (landedIds && landedIds.length > 0) {
        this.flashDropped(landedIds);
      }
    });
  }

  private flashDropped(ids: readonly string[]): void {
    if (this.droppedTimer !== null) clearTimeout(this.droppedTimer);
    this.justDropped.set(new Set(ids));
    this.droppedTimer = setTimeout(() => {
      this.zone.run(() => this.justDropped.set(new Set()));
      this.droppedTimer = null;
    }, DeckDragService._justDroppedMs);
  }

  private static candidateKey(rect: GridRect): string {
    return `${rect.x},${rect.y},${rect.w},${rect.h}`;
  }

  private static groupCandidateKey(dx: number, dy: number): string {
    return `group:${dx},${dy}`;
  }

  private static prefersReducedMotion(): boolean {
    return typeof window.matchMedia === 'function'
      && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }
}
