import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import {
  BORDER_ANIMATION_PERIOD_MS,
  resolveWidgetBorder,
  WIDGET_BORDER_WIDTH,
  WidgetBorder,
} from '@macro-deck/runtime';
import { ServerClockService } from '../../services/server-clock.service';

@Component({
  selector: 'shared-widget-border-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (resolved(); as border) {
      @for (anchor of anchors(); track anchor) {
        <div class="ring wb-{{ border.style }}" [style.--wb-color]="border.color" [style.--wb-phase]="anchor"></div>
      }
    }
  `,
  host: {
    '[style.--wb-width]': 'ringWidth',
  },
  styleUrls: ['./widget-border-overlay.component.scss'],
})
export class WidgetBorderOverlayComponent {
  private readonly clock = inject(ServerClockService);

  readonly border = input<WidgetBorder | undefined>(undefined);

  readonly resolved = computed(() => resolveWidgetBorder(this.border()));

  protected readonly anchors = computed<readonly string[]>(() => {
    this.resolved();

    return [`${-((this.clock.now() % BORDER_ANIMATION_PERIOD_MS) / 1000)}s`];
  });

  protected readonly ringWidth = `${WIDGET_BORDER_WIDTH}px`;
}
