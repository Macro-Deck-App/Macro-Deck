import { ChangeDetectionStrategy, Component, ElementRef, ViewChild, input, signal } from '@angular/core';

import { TooltipPlacement, computeTooltipPosition } from './tooltip-position';

@Component({
  selector: 'shared-tooltip',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true' },
  template: `
    <div #box class="tt" [class.tt-placed]="placed()"
         [style.top.px]="pos().top" [style.left.px]="pos().left">
      <span class="tt-label">{{ text() }}</span>
      @if (secondary()) { <span class="tt-secondary">{{ secondary() }}</span> }
    </div>
  `,
  styleUrls: ['./tooltip.component.scss'],
})
export class TooltipComponent {
  readonly text = input<string | null>(null);
  readonly secondary = input<string | null>(null);

  @ViewChild('box') private readonly box?: ElementRef<HTMLElement>;

  readonly pos = signal<{ top: number; left: number }>({ top: 0, left: 0 });
  readonly placed = signal(false);

  placeAgainst(anchor: HTMLElement, preferred: TooltipPlacement): void {
    const boxEl = this.box?.nativeElement;
    if (!boxEl) {
      return;
    }

    const size = { width: boxEl.offsetWidth, height: boxEl.offsetHeight };
    const viewport = { width: window.innerWidth, height: window.innerHeight };
    const { top, left } = computeTooltipPosition(anchor.getBoundingClientRect(), size, viewport, preferred);

    this.pos.set({ top, left });
    this.placed.set(true);
  }
}
