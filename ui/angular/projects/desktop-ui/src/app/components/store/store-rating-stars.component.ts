import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { formatStoreRating } from '../../util/store-rating-format';

export const STORE_STAR_PATH =
  'M12 2.6l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 17.45 6.2 20.5l1.1-6.45-4.7-4.6 6.5-.95z';

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
