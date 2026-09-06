import {
  ChangeDetectionStrategy,
  Component,
  ContentChild,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  TemplateRef,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';

import { OverlayPanelComponent } from '../overlay-panel/overlay-panel.component';

export interface ContextMenuItem {
  id: string;
  label: string;
  icon?: string;
  disabled?: boolean;
  danger?: boolean;
  dividerAfter?: boolean;
  children?: ContextMenuItem[];
  active?: boolean;
}

@Component({
  selector: 'shared-context-menu',
  standalone: true,
  imports: [CommonModule, OverlayPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './context-menu.component.html',
  styleUrls: ['./context-menu.component.scss'],
})
export class ContextMenuComponent implements OnChanges {
  @ContentChild('menuContent') menuContentTemplate?: TemplateRef<unknown>;

  @Input() isOpen = false;
  @Input() x = 0;
  @Input() y = 0;
  @Input() items: ContextMenuItem[] = [];
  @Input() minWidth = 180;

  @Output() itemClick = new EventEmitter<string>();
  @Output() closed = new EventEmitter<void>();

  readonly openChildId = signal<string | null>(null);
  private openChildAnchor: HTMLElement | null = null;
  private openTimer: ReturnType<typeof setTimeout> | null = null;
  private menuOpenedAt = 0;

  private static readonly HOVER_OPEN_DELAY_MS = 160;

  get openChildItems(): ContextMenuItem[] {
    return this.items.find(item => item.id === this.openChildId())?.children ?? [];
  }

  get openChildAnchorEl(): HTMLElement | null {
    return this.openChildAnchor;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['isOpen']) {
      return;
    }
    if (this.isOpen) {
      this.menuOpenedAt = Date.now();
    } else {
      this.closeChildren();
    }
  }

  onItemClick(item: ContextMenuItem, event: MouseEvent): void {
    if (item.disabled) {
      return;
    }
    if (item.children?.length) {
      this.toggleChildren(item, event.currentTarget as HTMLElement);
      return;
    }
    this.emitAndClose(item.id);
  }

  onChildClick(item: ContextMenuItem): void {
    if (item.disabled) {
      return;
    }
    this.emitAndClose(item.id);
  }

  onItemMouseEnter(item: ContextMenuItem, event: MouseEvent): void {
    this.clearOpenTimer();
    if (!item.children?.length) {
      this.closeChildren();
      return;
    }
    if (this.openChildId() === item.id) {
      return;
    }
    this.requestOpenChildren(item, event.currentTarget as HTMLElement);
  }

  onItemKeyDown(item: ContextMenuItem, event: KeyboardEvent): void {
    if (event.key === 'ArrowRight' && item.children?.length) {
      event.preventDefault();
      this.requestOpenChildren(item, event.currentTarget as HTMLElement);
    } else if (event.key === 'ArrowLeft' && this.openChildId() === item.id) {
      event.preventDefault();
      this.closeChildren();
    }
  }

  onOuterDismissed(): void {
    this.closeChildren();
    this.closed.emit();
  }

  onFlyoutDismissed(): void {
    this.closeChildren();
  }

  private emitAndClose(id: string): void {
    this.itemClick.emit(id);
    this.closeChildren();
    this.closed.emit();
  }

  private toggleChildren(item: ContextMenuItem, anchor: HTMLElement): void {
    this.clearOpenTimer();
    if (this.openChildId() === item.id) {
      this.closeChildren();
    } else {
      this.requestOpenChildren(item, anchor);
    }
  }

  private requestOpenChildren(item: ContextMenuItem, anchor: HTMLElement): void {
    this.clearOpenTimer();
    const remaining = Math.max(0, ContextMenuComponent.HOVER_OPEN_DELAY_MS - (Date.now() - this.menuOpenedAt));
    if (remaining === 0) {
      this.openChildren(item, anchor);
      return;
    }
    this.openTimer = setTimeout(() => {
      this.openTimer = null;
      this.openChildren(item, anchor);
    }, remaining);
  }

  private openChildren(item: ContextMenuItem, anchor: HTMLElement): void {
    this.openChildAnchor = anchor;
    this.openChildId.set(item.id);
  }

  private closeChildren(): void {
    this.clearOpenTimer();
    this.openChildAnchor = null;
    this.openChildId.set(null);
  }

  private clearOpenTimer(): void {
    if (this.openTimer !== null) {
      clearTimeout(this.openTimer);
      this.openTimer = null;
    }
  }
}
