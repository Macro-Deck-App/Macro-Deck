import {
  ComponentRef,
  Directive,
  ElementRef,
  HostListener,
  Input,
  OnChanges,
  OnDestroy,
  SimpleChanges,
  ViewContainerRef,
  inject,
} from '@angular/core';

import { TooltipPlacement } from './tooltip-position';
import { TooltipComponent } from './tooltip.component';

export const TOOLTIP_SHOW_DELAY_MS = 300;

let visible: TooltipDirective | null = null;

@Directive({
  selector: '[sharedTooltip]',
  standalone: true,
})
export class TooltipDirective implements OnChanges, OnDestroy {
  @Input() sharedTooltip: string | null = null;
  @Input() tooltipSecondary: string | null = null;
  @Input() tooltipDisabled = false;
  @Input() tooltipPlacement: TooltipPlacement = 'right';

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly vcr = inject(ViewContainerRef);

  private ref: ComponentRef<TooltipComponent> | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private listening = false;
  private hovered = false;
  private focused = false;

  private readonly onKeyDown = (event: KeyboardEvent): void => {
    // Must not preventDefault/stopPropagation: a modal's own Escape handling still needs to fire.
    if (event.key === 'Escape') {
      this.hide();
    }
  };

  private readonly onWindowBlur = (): void => {
    this.hide();
  };

  private readonly onReposition = (): void => {
    this.paint();
  };

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['tooltipDisabled'] && !changes['sharedTooltip'] && !changes['tooltipSecondary']) {
      return;
    }
    if (this.tooltipDisabled || !this.sharedTooltip) {
      this.hide();
      return;
    }
    if (this.ref) {
      this.ref.setInput('text', this.sharedTooltip);
      this.ref.setInput('secondary', this.tooltipSecondary);
      this.paint();
    }
  }

  ngOnDestroy(): void {
    this.hide();
  }

  @HostListener('mouseenter')
  onPointerEnter(): void {
    this.hovered = true;
    this.scheduleShow();
  }

  @HostListener('focus')
  onFocus(): void {
    this.focused = true;
    this.scheduleShow();
  }

  @HostListener('mouseleave')
  onPointerLeave(): void {
    this.hovered = false;
    this.hideUnlessHeld();
  }

  @HostListener('blur')
  onBlur(): void {
    this.focused = false;
    this.hideUnlessHeld();
  }

  @HostListener('click')
  onClick(): void {
    this.hide();
  }

  private scheduleShow(): void {
    if (this.tooltipDisabled || !this.sharedTooltip || this.ref || this.timer !== null) return;
    this.timer = setTimeout(() => { this.timer = null; this.show(); }, TOOLTIP_SHOW_DELAY_MS);
  }

  private show(): void {
    visible?.hide();
    visible = this;
    this.ref = this.vcr.createComponent(TooltipComponent);
    this.ref.setInput('text', this.sharedTooltip);
    this.ref.setInput('secondary', this.tooltipSecondary);
    this.paint();
    this.attachGlobalListeners();
  }

  private paint(): void {
    if (!this.ref) return;
    this.ref.changeDetectorRef.detectChanges();   // render, so the box has a size to measure
    this.ref.instance.placeAgainst(this.host.nativeElement, this.tooltipPlacement);
    this.ref.changeDetectorRef.detectChanges();   // apply top/left + .tt-placed
  }

  private hideUnlessHeld(): void {
    // Hover and focus hold the tooltip open independently: sweeping the pointer across a
    // focused item and away must not take down the tooltip its focus is still holding.
    if (this.hovered || this.focused) return;
    this.hide();
  }

  private hide(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
    this.detachGlobalListeners();
    this.ref?.destroy();
    this.ref = null;
    if (visible === this) {
      visible = null;
    }
  }

  private attachGlobalListeners(): void {
    if (this.listening) return;
    this.listening = true;
    document.addEventListener('keydown', this.onKeyDown);
    window.addEventListener('blur', this.onWindowBlur);
    window.addEventListener('scroll', this.onReposition, true);
    window.addEventListener('resize', this.onReposition);
  }

  private detachGlobalListeners(): void {
    if (!this.listening) return;
    this.listening = false;
    document.removeEventListener('keydown', this.onKeyDown);
    window.removeEventListener('blur', this.onWindowBlur);
    window.removeEventListener('scroll', this.onReposition, true);
    window.removeEventListener('resize', this.onReposition);
  }
}
