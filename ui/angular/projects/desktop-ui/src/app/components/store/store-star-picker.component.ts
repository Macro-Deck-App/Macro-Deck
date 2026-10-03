import { ChangeDetectionStrategy, Component, inject, input, model } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { STORE_STAR_PATH } from './store-rating-stars.component';

const STAR_VALUES = [1, 2, 3, 4, 5] as const;

@Component({
  selector: 'app-store-star-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="star-picker"
      role="radiogroup"
      [attr.aria-labelledby]="labelledBy()"
      [attr.aria-disabled]="disabled() || null"
      [attr.aria-invalid]="invalid() ? 'true' : null">
      @for (star of starValues; track star) {
        <button
          type="button"
          class="star-option"
          role="radio"
          [attr.data-star]="star"
          [class.is-lit]="(value() ?? 0) >= star"
          [attr.aria-checked]="value() === star"
          [attr.aria-label]="label(star)"
          [attr.tabindex]="tabIndex(star)"
          [disabled]="disabled()"
          (click)="select(star)"
          (keydown)="onKeydown($event)">
          <svg class="star-glyph" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
            <path [attr.d]="starPath" />
          </svg>
        </button>
      }
    </div>
  `,
  styleUrls: ['./store-star-picker.component.scss'],
})
export class StoreStarPickerComponent {
  private readonly localization = inject(LocalizationService);

  readonly value = model<number | null>(null);
  readonly disabled = input(false);
  readonly invalid = input(false);
  readonly labelledBy = input<string | null>(null);

  protected readonly starValues = STAR_VALUES;
  protected readonly starPath = STORE_STAR_PATH;

  protected label(stars: number): string {
    return this.localization.translateKey(AppStrings.Store.Reviews.StarCount, { count: stars });
  }

  protected tabIndex(star: number): number {
    return (this.value() ?? 1) === star ? 0 : -1;
  }

  protected select(star: number): void {
    if (!this.disabled()) {
      this.value.set(star);
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    const current = this.value() ?? 0;
    const step = event.key === 'ArrowRight' || event.key === 'ArrowUp' ? 1
      : event.key === 'ArrowLeft' || event.key === 'ArrowDown' ? -1
        : 0;
    let next: number | null = null;
    if (step !== 0) {
      next = ((current - 1 + step + STAR_VALUES.length) % STAR_VALUES.length) + 1;
    } else if (event.key === 'Home') {
      next = 1;
    } else if (event.key === 'End') {
      next = STAR_VALUES.length;
    }
    if (next === null) {
      return;
    }

    event.preventDefault();
    this.select(next);
    const group = (event.currentTarget as HTMLElement).parentElement;
    group?.querySelector<HTMLElement>(`[data-star="${next}"]`)?.focus();
  }
}
