import { Injectable, NgZone, OnDestroy, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { Folder, FolderDragState, FolderDropPosition, FolderMoveRequest } from '@macro-deck/runtime';
import { ProfileService } from '@shared';

const DRAG_THRESHOLD_PX = 5;
const SCROLL_EDGE = 48;
const SCROLL_SPEED = 9;

interface FolderDragSession {
  folder: Folder;
  hostEl: HTMLElement;
  pointerId: number;
  startX: number;
  startY: number;
  active: boolean;
}

interface FolderDropHit {
  targetId: string;
  position: FolderDropPosition;
  rootEnd: boolean;
}

// HTML5 drag-and-drop is off the table: the Tauri shell owns WebView drag-and-drop so file/icon drop
// targets get real filesystem paths, and that ownership swallows the HTML5 drag sequence entirely in
// packaged builds (issue #281). Pointer events never touch that pipeline.
@Injectable()
export class FolderDragService implements OnDestroy {
  private readonly zone = inject(NgZone);
  private readonly profileService = inject(ProfileService);

  readonly state = signal<FolderDragState>({
    isDragging: false,
    folderId: null,
    targetFolderId: null,
    dropPosition: null
  });
  readonly ghostLabel = signal('');
  readonly ghostX = signal(0);
  readonly ghostY = signal(0);
  readonly dropAtRootEnd = signal(false);
  readonly dropped = new Subject<FolderMoveRequest>();

  private session: FolderDragSession | null = null;
  private listEl: HTMLElement | null = null;
  private scrollRaf: number | null = null;
  private lastPointer = { x: 0, y: 0 };
  private suppressNextClick = false;

  private readonly onMove = (e: PointerEvent) => this.handleMove(e);
  private readonly onUp = () => this.handleUp();
  private readonly onCancel = () => this.cancel();
  private readonly onKey = (e: KeyboardEvent) => {
    if (e.key === 'Escape') this.cancel();
  };

  attach(listEl: HTMLElement): void {
    this.listEl = listEl;
  }

  consumeClickSuppression(): boolean {
    const suppressed = this.suppressNextClick;
    this.suppressNextClick = false;
    return suppressed;
  }

  press(event: PointerEvent, folder: Folder, hostEl: HTMLElement): void {
    if (event.button !== 0 || this.session) return;
    const target = event.target as HTMLElement;
    if (target.closest('.expand-btn')) return;
    if (this.profileService.isCurrentProfileLocked()) return;

    this.suppressNextClick = false;

    this.session = {
      folder,
      hostEl,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      active: false
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
      if (Math.hypot(dx, dy) < DRAG_THRESHOLD_PX) return;
      this.activate(session);
    }

    event.preventDefault();
    const hit = this.hitTest(event.clientY);
    this.zone.run(() => {
      this.ghostX.set(event.clientX);
      this.ghostY.set(event.clientY);
      this.dropAtRootEnd.set(hit?.rootEnd ?? false);
      this.state.update(current => ({
        ...current,
        targetFolderId: hit?.targetId ?? null,
        dropPosition: hit?.position ?? null
      }));
    });
  }

  private activate(session: FolderDragSession): void {
    session.active = true;
    try {
      session.hostEl.setPointerCapture(session.pointerId);
    } catch {
    }

    this.zone.run(() => {
      this.state.set({
        isDragging: true,
        folderId: session.folder.id,
        targetFolderId: null,
        dropPosition: null
      });
      this.ghostLabel.set(session.folder.name);
      this.ghostX.set(this.lastPointer.x);
      this.ghostY.set(this.lastPointer.y);
    });
    this.startAutoScroll();
  }

  private handleUp(): void {
    const session = this.session;
    if (!session) return;

    if (!session.active) {
      this.finish();
      return;
    }

    this.suppressNextClick = true;
    const current = this.state();
    const request: FolderMoveRequest | null = current.targetFolderId && current.dropPosition
      ? { folderId: session.folder.id, targetId: current.targetFolderId, position: current.dropPosition }
      : null;

    this.finish();

    if (request) {
      this.dropped.next(request);
    }
  }

  private cancel(): void {
    if (this.session?.active) this.suppressNextClick = true;
    this.finish();
  }

  private finish(): void {
    this.stopListening();
    this.session = null;
    this.zone.run(() => {
      this.state.set({ isDragging: false, folderId: null, targetFolderId: null, dropPosition: null });
      this.dropAtRootEnd.set(false);
    });
  }

  private stopListening(): void {
    document.removeEventListener('pointermove', this.onMove);
    document.removeEventListener('pointerup', this.onUp);
    document.removeEventListener('pointercancel', this.onCancel);
    document.removeEventListener('keydown', this.onKey);
    this.stopAutoScroll();
  }

  private hitTest(clientY: number): FolderDropHit | null {
    const rows = this.visibleRows();
    if (rows.length === 0) return null;

    const firstRect = rows[0].getBoundingClientRect();
    if (clientY < firstRect.top) {
      const firstId = rows[0].dataset['folderId'];
      return firstId ? { targetId: firstId, position: 'before', rootEnd: false } : null;
    }

    for (const row of rows) {
      const rect = row.getBoundingClientRect();
      if (clientY > rect.bottom) continue;
      const folderId = row.dataset['folderId'];
      if (!folderId) continue;
      const ratio = (clientY - rect.top) / rect.height;
      const position: FolderDropPosition = ratio < 0.25 ? 'before' : ratio > 0.75 ? 'after' : 'inside';
      return { targetId: folderId, position, rootEnd: false };
    }

    const roots = rows.filter(row => row.dataset['depth'] === '0');
    const lastRoot = roots[roots.length - 1];
    const lastRootId = lastRoot?.dataset['folderId'];
    return lastRootId ? { targetId: lastRootId, position: 'after', rootEnd: true } : null;
  }

  private visibleRows(): HTMLElement[] {
    const listEl = this.listEl;
    const session = this.session;
    if (!listEl || !session) return [];
    return Array.from(listEl.querySelectorAll<HTMLElement>('.folder-item[data-folder-id]'))
      .filter(row => !session.hostEl.contains(row));
  }

  private startAutoScroll(): void {
    const listEl = this.listEl;
    if (!listEl) return;
    const step = () => {
      if (!this.session?.active) return;
      const rect = listEl.getBoundingClientRect();
      const { y } = this.lastPointer;
      if (y < rect.top + SCROLL_EDGE && listEl.scrollTop > 0) {
        listEl.scrollTop -= SCROLL_SPEED;
      } else if (y > rect.bottom - SCROLL_EDGE) {
        listEl.scrollTop += SCROLL_SPEED;
      }
      this.scrollRaf = requestAnimationFrame(step);
    };
    this.zone.runOutsideAngular(() => {
      this.scrollRaf = requestAnimationFrame(step);
    });
  }

  private stopAutoScroll(): void {
    if (this.scrollRaf !== null) {
      cancelAnimationFrame(this.scrollRaf);
      this.scrollRaf = null;
    }
  }
}
