import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  NgZone,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
  inject,
  signal,
} from '@angular/core';

@Component({
  selector: 'shared-overlay-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rendered()) {
      <div
        #panel
        class="op-panel"
        [class.op-panel-closing]="closing()"
        [style.top.px]="pos().top"
        [style.left.px]="pos().left"
        [style.width.px]="pos().width"
        [style.min-width.px]="minWidth"
        [style.max-height.px]="maxHeight"
        [style.overflow-y]="maxHeight === null ? 'visible' : null">
        <ng-content />
      </div>
    }
  `,
  styleUrls: ['./overlay-panel.component.scss'],
})
export class OverlayPanelComponent implements AfterViewChecked, OnChanges, OnDestroy {
  @Input() anchor: HTMLElement | null = null;
  @Input() x: number | null = null;
  @Input() y: number | null = null;
  @Input() isOpen = false;
  @Input() matchAnchorWidth = false;
  @Input() align: 'start' | 'end' = 'start';
  @Input() placement: 'below' | 'right-start' = 'below';
  @Input() minWidth: number | null = null;
  @Input() maxHeight: number | null = 224;
  @Input() animateExit = false;
  @Input() anchorOffset = 4;

  @Output() dismissed = new EventEmitter<void>();

  @ViewChild('panel') private panelRef?: ElementRef<HTMLElement>;

  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly zone = inject(NgZone);

  private static readonly _viewportPadding = 8;
  private static readonly _exitDurationMs = 150;

  private static readonly openInstances = new Set<OverlayPanelComponent>();

  static isAnyOpen(): boolean {
    return OverlayPanelComponent.openInstances.size > 0;
  }

  readonly pos = signal<{ top: number; left: number; width: number | null }>({
    top: 0,
    left: 0,
    width: null,
  });

  readonly rendered = signal(false);
  readonly closing = signal(false);

  private _exitTimer: ReturnType<typeof setTimeout> | null = null;
  private listening = false;

  private readonly onPointerDown = (event: MouseEvent): void => {
    const target = event.target as Node;

    // The panel is checked separately from the host because it does not always live inside it: when
    // an ancestor would clip it, it is moved out to the body (see ngAfterViewChecked). Testing only
    // the host then reads a click on the panel's own items as a click outside, and the panel is
    // dismissed on mousedown - before the click that would have activated the item ever lands.
    const panel = this.panelRef?.nativeElement;
    if (this.host.nativeElement.contains(target) ||
      panel?.contains(target) ||
      this.anchor?.contains(target)) {
      return;
    }

    this.zone.run(() => this.dismissed.emit());
  };

  private readonly onKeyDown = (event: KeyboardEvent): void => {
    if (event.key === 'Escape') {
      this.zone.run(() => this.dismissed.emit());
    }
  };

  private readonly onReposition = (): void => {
    if (this.isOpen) {
      this.zone.run(() => this.updatePosition());
    }
  };

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] || changes['x'] || changes['y'] || changes['anchor'] || changes['placement']) {
      if (this.isOpen) {
        OverlayPanelComponent.openInstances.add(this);
        this.clearExitTimer();
        this.closing.set(false);
        this.rendered.set(true);
        this.updatePosition();
        this.attachListeners();
        requestAnimationFrame(() => this.updatePosition());
      } else {
        OverlayPanelComponent.openInstances.delete(this);
        this.detachListeners();
        if (this.animateExit && this.rendered()) {
          this.closing.set(true);
          this._exitTimer = setTimeout(() => {
            this._exitTimer = null;
            this.closing.set(false);
            this.rendered.set(false);
          }, OverlayPanelComponent._exitDurationMs);
        } else {
          this.closing.set(false);
          this.rendered.set(false);
        }
      }
    }
  }

  ngAfterViewChecked(): void {
    const panel = this.panelRef?.nativeElement;
    if (!panel || panel.parentElement === document.body) {
      return;
    }

    // Only when an ancestor actually traps it. Everywhere else the panel stays exactly where it was
    // rendered, so nothing about the common case - or how anything addresses it - changes.
    if (this.containingBlockAncestor(panel.parentElement)) {
      document.body.appendChild(panel);
      this.updatePosition();
    }
  }

  ngOnDestroy(): void {
    OverlayPanelComponent.openInstances.delete(this);
    this.clearExitTimer();
    this.detachListeners();

    // Re-parented above, so the view's own teardown no longer reaches it.
    this.panelRef?.nativeElement.remove();
  }

  private clearExitTimer(): void {
    if (this._exitTimer !== null) {
      clearTimeout(this._exitTimer);
      this._exitTimer = null;
    }
  }

  private attachListeners(): void {
    if (this.listening) {
      return;
    }
    this.listening = true;
    this.zone.runOutsideAngular(() => {
      // Capture phase: Tauri's injected drag-region script listens for `mousedown` on `document`
      // and calls `stopImmediatePropagation()`, so a bubble listener never sees clicks on the
      // titlebar and the panel would stay open there.
      document.addEventListener('mousedown', this.onPointerDown, true);
      document.addEventListener('keydown', this.onKeyDown);
      window.addEventListener('scroll', this.onReposition, true);
      window.addEventListener('resize', this.onReposition);
    });
  }

  private detachListeners(): void {
    if (!this.listening) {
      return;
    }
    this.listening = false;
    document.removeEventListener('mousedown', this.onPointerDown, true);
    document.removeEventListener('keydown', this.onKeyDown);
    window.removeEventListener('scroll', this.onReposition, true);
    window.removeEventListener('resize', this.onReposition);
  }

  private updatePosition(): void {
    const padding = OverlayPanelComponent._viewportPadding;
    const panelEl = this.panelRef?.nativeElement;
    const panelHeight = panelEl?.offsetHeight ?? Math.min(this.maxHeight ?? 200, 200);
    const panelWidth = panelEl?.offsetWidth ?? this.minWidth ?? 200;

    let top: number;
    let left: number;
    let width: number | null = null;

    if (this.x !== null && this.y !== null) {
      left = this.x;
      top = this.y;
      if (top + panelHeight + padding > window.innerHeight) {
        top = this.y - panelHeight;
      }
      if (left + panelWidth + padding > window.innerWidth) {
        left = this.x - panelWidth;
      }
    } else if (this.anchor) {
      const rect = this.anchor.getBoundingClientRect();
      width = this.matchAnchorWidth ? rect.width : null;
      const effectiveWidth = width ?? panelWidth;

      if (this.placement === 'right-start') {
        left = rect.right + this.anchorOffset;
        if (left + effectiveWidth + padding > window.innerWidth) {
          const flipped = rect.left - effectiveWidth - this.anchorOffset;
          left = flipped > padding ? flipped : window.innerWidth - effectiveWidth - padding;
        }
        top = rect.top;
        if (top + panelHeight + padding > window.innerHeight) {
          top = window.innerHeight - panelHeight - padding;
        }
      } else {
        left = this.align === 'end' ? rect.right - effectiveWidth : rect.left;
        top = rect.bottom + this.anchorOffset;
        if (top + panelHeight + padding > window.innerHeight
          && rect.top - panelHeight - this.anchorOffset > padding) {
          top = rect.top - panelHeight - this.anchorOffset;
        }
        if (left + effectiveWidth + padding > window.innerWidth) {
          left = window.innerWidth - effectiveWidth - padding;
        }
      }
    } else {
      return;
    }

    // Clamped against the viewport first, then rebased: the clamp is what keeps a panel on screen,
    // and it is only meaningful in viewport coordinates.
    top = Math.max(padding, top);
    left = Math.max(padding, left);

    const origin = this.fixedContainingBlockOrigin();

    this.pos.set({
      top: top - origin.y,
      left: left - origin.x,
      width,
    });
  }

  private fixedContainingBlockOrigin(): { x: number; y: number } {
    // Walked from the panel's own parent, not the host's: once the panel has been moved out to
    // `document.body` the host is still sitting inside the ancestor that trapped it, and rebasing
    // off the host would then re-introduce the very offset the move just removed.
    const from = this.panelRef?.nativeElement.parentElement ?? this.host.nativeElement.parentElement;
    const ancestor = this.containingBlockAncestor(from);
    if (!ancestor) {
      return { x: 0, y: 0 };
    }

    // The containing block is the ancestor's padding box; getBoundingClientRect gives its border box.
    const style = getComputedStyle(ancestor);
    const rect = ancestor.getBoundingClientRect();
    return {
      x: rect.left + parseFloat(style.borderLeftWidth || '0'),
      y: rect.top + parseFloat(style.borderTopWidth || '0'),
    };
  }

  private containingBlockAncestor(from: HTMLElement | null): HTMLElement | null {
    for (let el = from; el && el !== document.body; el = el.parentElement) {
      const style = getComputedStyle(el);
      if (style.transform !== 'none' ||
        style.perspective !== 'none' ||
        style.filter !== 'none' ||
        style.backdropFilter !== 'none' ||
        /\b(transform|perspective|filter)\b/.test(style.willChange) ||
        /\b(layout|paint|strict|content)\b/.test(style.contain)) {
        return el;
      }
    }

    return null;
  }
}
