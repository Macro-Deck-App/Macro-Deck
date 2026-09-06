import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ContentChild,
  EventEmitter,
  HostListener,
  Input,
  OnDestroy,
  Output,
  TemplateRef,
  booleanAttribute,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { OverlayPanelComponent } from '@shared';

export type DropdownPosition = 'bottom-left' | 'bottom-right' | 'top-left' | 'top-right';

export type DropdownTriggerVariant = 'default' | 'primary';

@Component({
  selector: 'shared-dropdown-menu',
  standalone: true,
  imports: [CommonModule, OverlayPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dropdown-menu.component.html',
  styleUrls: ['./dropdown-menu.component.scss'],
  host: { '[class.dm-fill]': 'fill' },
})
export class DropdownMenuComponent implements OnDestroy {
  @ContentChild('triggerContent') triggerTemplate?: TemplateRef<unknown>;
  @ContentChild('menuContent') menuContentTemplate?: TemplateRef<unknown>;

  @Input() isOpen = false;
  @Input() position: DropdownPosition = 'bottom-right';
  @Input() minWidth = 200;
  @Input() maxHeight: number | null = null;
  @Input() triggerIcon = 'icon-dots-vertical';
  @Input() triggerLabel?: string;
  @Input() triggerVariant: DropdownTriggerVariant = 'default';
  @Input() triggerAriaLabel?: string;
  @Input() openOnHover = false;
  @Input() animateExit = false;
  @Input({ transform: booleanAttribute }) fill = false;
  @Input() anchorOffset = 4;
  @Input({ transform: booleanAttribute }) closeOnItemClick = false;

  @Output() isOpenChange = new EventEmitter<boolean>();
  @Output() opened = new EventEmitter<void>();
  @Output() closed = new EventEmitter<void>();

  private readonly cdr = inject(ChangeDetectorRef);

  private static readonly _hoverCloseDelayMs = 150;
  private _hoverCloseTimer: ReturnType<typeof setTimeout> | null = null;

  get align(): 'start' | 'end' {
    return this.position.endsWith('right') ? 'end' : 'start';
  }

  ngOnDestroy(): void {
    this.clearHoverCloseTimer();
  }

  @HostListener('mouseenter')
  onMouseEnter(): void {
    if (!this.openOnHover) {
      return;
    }
    this.clearHoverCloseTimer();
    this.open();
  }

  @HostListener('mouseleave')
  onMouseLeave(): void {
    if (!this.openOnHover) {
      return;
    }
    this.clearHoverCloseTimer();
    this._hoverCloseTimer = setTimeout(() => {
      this._hoverCloseTimer = null;
      this.close();
      this.cdr.markForCheck();
    }, DropdownMenuComponent._hoverCloseDelayMs);
  }

  toggle(event?: MouseEvent): void {
    event?.stopPropagation();
    if (this.openOnHover) {
      this.open();
      return;
    }
    if (this.isOpen) {
      this.close();
    } else {
      this.open();
    }
  }

  open(): void {
    if (this.isOpen) {
      return;
    }
    this.isOpen = true;
    this.isOpenChange.emit(true);
    this.opened.emit();
  }

  protected onContentClick(event: MouseEvent): void {
    if (!this.closeOnItemClick) {
      return;
    }

    const target = event.target as HTMLElement | null;
    if (target?.closest('button:not([disabled]), [role="menuitem"]:not([aria-disabled="true"])')) {
      this.close();
    }
  }

  close(): void {
    if (!this.isOpen) {
      return;
    }
    this.isOpen = false;
    this.isOpenChange.emit(false);
    this.closed.emit();
  }

  private clearHoverCloseTimer(): void {
    if (this._hoverCloseTimer !== null) {
      clearTimeout(this._hoverCloseTimer);
      this._hoverCloseTimer = null;
    }
  }
}
