import { Component, Input, Output, EventEmitter, ElementRef, inject, ChangeDetectionStrategy } from '@angular/core';

import { Folder, FolderDropPosition, FolderMoveDirection } from '@macro-deck/runtime';
import { TranslatePipe } from '@shared';
import { FolderDragService } from '../../../../services/folder-drag.service';

type FolderNavigateDirection = 'up' | 'down' | 'left' | 'right' | 'home' | 'end';

const ALT_ARROW_DIRECTIONS: Record<string, FolderMoveDirection> = {
  ArrowUp: 'up',
  ArrowDown: 'down',
  ArrowRight: 'into',
  ArrowLeft: 'out'
};

const NAV_KEYS: Record<string, FolderNavigateDirection> = {
  ArrowUp: 'up',
  ArrowDown: 'down',
  ArrowLeft: 'left',
  ArrowRight: 'right',
  Home: 'home',
  End: 'end'
};

@Component({
  selector: 'app-folder-item',
  standalone: true,
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './folder-item.component.html',
  styleUrls: ['./folder-item.component.scss']
})
export class FolderItemComponent {
  @Input({ required: true }) folder!: Folder;
  @Input() depth = 0;
  @Input() isSelected = false;
  @Input() isDragging = false;
  @Input() hasChildren = false;
  @Input() dropPosition: FolderDropPosition | null = null;

  @Output() select = new EventEmitter<string>();
  @Output() toggleExpand = new EventEmitter<string>();
  @Output() contextMenu = new EventEmitter<{ folderId: string; x: number; y: number }>();
  @Output() move = new EventEmitter<{ folderId: string; direction: FolderMoveDirection }>();
  @Output() navigate = new EventEmitter<{ folderId: string; direction: FolderNavigateDirection }>();

  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private readonly drag = inject(FolderDragService, { optional: true });

  onPointerDown(event: PointerEvent): void {
    this.drag?.press(event, this.folder, this.elementRef.nativeElement);
  }

  onClick(event: MouseEvent): void {
    event.stopPropagation();
    // A drag that started on this row must not also select it.
    if (this.drag?.consumeClickSuppression()) return;
    this.select.emit(this.folder.id);
  }

  onToggleExpand(event: MouseEvent): void {
    event.stopPropagation();
    this.toggleExpand.emit(this.folder.id);
  }

  onContextMenu(event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.contextMenu.emit({
      folderId: this.folder.id,
      x: event.clientX,
      y: event.clientY
    });
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.target !== event.currentTarget) return;

    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.select.emit(this.folder.id);
      return;
    }

    if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) {
      event.preventDefault();
      const rect = this.rowRect();
      this.contextMenu.emit({ folderId: this.folder.id, x: rect.left, y: rect.bottom });
      return;
    }

    if (event.altKey) {
      const direction = ALT_ARROW_DIRECTIONS[event.key];
      if (direction) {
        event.preventDefault();
        this.move.emit({ folderId: this.folder.id, direction });
      }
      return;
    }

    const direction = NAV_KEYS[event.key];
    if (direction) {
      event.preventDefault();
      this.navigate.emit({ folderId: this.folder.id, direction });
    }
  }

  private rowRect(): DOMRect {
    const row = this.elementRef.nativeElement.querySelector('.folder-item');
    return (row ?? this.elementRef.nativeElement).getBoundingClientRect();
  }
}
