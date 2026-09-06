import { ChangeDetectionStrategy, Component, OnDestroy, computed, inject } from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { ScreenCursorService } from '../../services/screen-cursor.service';

@Component({
  selector: 'shared-cursor-position',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (supported) {
      <span class="cursor-position" [title]="tooltip()">
        <span class="icon icon-crosshair icon-sm" aria-hidden="true"></span>
        @if (position(); as point) {
          <span class="cursor-position-value">{{ point.x }}, {{ point.y }}</span>
        } @else {
          <span class="cursor-position-value cursor-position-empty">-, -</span>
        }
      </span>
    }
  `,
  styleUrls: ['./cursor-position.component.scss'],
})
export class CursorPositionComponent implements OnDestroy {
  private readonly cursor = inject(ScreenCursorService);
  private readonly localization = inject(LocalizationService);

  readonly supported = this.cursor.supported;
  readonly position = this.cursor.position;
  readonly tooltip = computed(() =>
    this.localization.translateKey(AppStrings.Widgets.CursorPositionTooltip));

  private readonly release = this.cursor.track();

  ngOnDestroy(): void {
    this.release();
  }
}
