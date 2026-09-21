import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { formatStoreRating } from '../../util/store-rating-format';

export const STORE_STAR_PATH =
  'M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z';

type StarFill = 'full' | 'half' | 'empty';

let nextClipId = 0;

@Component({
  selector: 'shared-store-rating-stars',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span [class]="'stars stars--' + size()" role="img" [attr.aria-label]="label()">
      @for (fill of fills(); track $index) {
        <svg class="star" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          @if (fill === 'half') {
            <defs>
              <clipPath [attr.id]="clipId"><rect x="0" y="0" width="12" height="24" /></clipPath>
            </defs>
            <path class="star-empty" [attr.d]="path" />
            <path class="star-full" [attr.d]="path" [attr.clip-path]="'url(#' + clipId + ')'" />
          } @else {
            <path [class]="fill === 'full' ? 'star-full' : 'star-empty'" [attr.d]="path" />
          }
        </svg>
      }
    </span>
  `,
  styleUrls: ['./store-rating-stars.component.scss'],
})
export class StoreRatingStarsComponent {
  private readonly localization = inject(LocalizationService);

  readonly rating = input.required<number>();
  readonly size = input<'xs' | 'sm' | 'md'>('xs');

  protected readonly path = STORE_STAR_PATH;
  protected readonly clipId = `store-star-half-${nextClipId++}`;

  protected readonly fills = computed<StarFill[]>(() => {
    const rounded = Math.round(Math.min(5, Math.max(0, this.rating())) * 2) / 2;
    return [1, 2, 3, 4, 5].map(star => rounded >= star ? 'full' : rounded >= star - 0.5 ? 'half' : 'empty');
  });

  protected readonly label = computed(() => this.localization.translateKey(AppStrings.Store.Reviews.StarsLabel, {
    rating: formatStoreRating(this.rating(), this.localization.culture()),
  }));
}
