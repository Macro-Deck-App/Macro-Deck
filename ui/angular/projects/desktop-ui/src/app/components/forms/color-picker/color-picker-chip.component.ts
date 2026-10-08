import { ChangeDetectionStrategy, Component, ElementRef, input, output, viewChild } from '@angular/core';
import { SelectCaretComponent } from '../select-caret/select-caret.component';

let nextChipId = 0;

@Component({
  selector: 'shared-color-picker-chip',
  standalone: true,
  imports: [SelectCaretComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      #button
      class="cp-chip"
      type="button"
      aria-haspopup="dialog"
      [disabled]="disabled()"
      [attr.aria-expanded]="expanded()"
      [attr.title]="label()"
      [attr.aria-describedby]="count() > 0 ? countId : null"
      (click)="activate.emit()">
      @if (resetGlyph()) {
        <svg
          class="cp-chip-glyph"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true">
          <path d="M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8" />
          <path d="M21 3v5h-5" />
        </svg>
      } @else {
        <span
          class="cp-chip-swatch"
          aria-hidden="true"
          [class.cp-chip-swatch-empty]="swatch() === null"
          [style.--cp-fill]="swatch()"></span>
      }
      <span
        class="cp-chip-label"
        [class.cp-chip-missing]="missing()"
        [class.cp-chip-placeholder]="placeholder()">{{ label() }}</span>
      @if (count() > 0) {
        <span class="cp-chip-badge" aria-hidden="true">{{ count() }}</span>
        <span [id]="countId" hidden>{{ countText() }}</span>
      }
      <shared-select-caret />
    </button>
  `,
  styleUrls: ['./color-picker-chip.component.scss'],
})
export class ColorPickerChipComponent {
  readonly swatch = input<string | null>(null);
  readonly label = input('');
  readonly placeholder = input(false);
  readonly resetGlyph = input(false);
  readonly missing = input(false);
  readonly count = input(0);
  readonly countText = input('');
  readonly expanded = input(false);
  readonly disabled = input(false);

  readonly activate = output<void>();

  readonly button = viewChild.required<ElementRef<HTMLButtonElement>>('button');

  protected readonly countId = `cp-modifier-count-${++nextChipId}`;

  focus(): void {
    this.button().nativeElement.focus();
  }
}
