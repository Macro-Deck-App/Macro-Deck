import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export interface ColorPickerResultStep {
  icon: string;
  summary: string;
}

@Component({
  selector: 'shared-color-picker-result',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="cpr-colors">
      <div class="cpr-item">
        <span class="cpr-label">{{ originalLabel() }}</span>
        <div class="cpr-value">
          <span
            class="cpr-swatch cpr-original"
            aria-hidden="true"
            [class.cpr-swatch-empty]="original() === null"
            [style.--cpr-fill]="original()"></span>
          <span class="cpr-hex">{{ original()?.toUpperCase() ?? '-' }}</span>
        </div>
      </div>
      <span class="icon icon-sm icon-arrow-right cpr-arrow" aria-hidden="true"></span>
      <div class="cpr-item">
        <span class="cpr-label">{{ resultLabel() }}</span>
        <div class="cpr-value">
          <span
            class="cpr-swatch cpr-resolved"
            aria-hidden="true"
            [class.cpr-swatch-empty]="result() === null"
            [style.--cpr-fill]="result()"></span>
          <span class="cpr-hex">{{ result()?.toUpperCase() ?? '-' }}</span>
        </div>
      </div>
    </div>
    @if (steps().length > 0) {
      <ul class="cpr-summary">
        @for (step of steps(); track $index) {
          <li class="cpr-summary-item">
            <span class="icon icon-sm icon-{{ step.icon }}" aria-hidden="true"></span>
            <span>{{ step.summary }}</span>
          </li>
        }
      </ul>
    }
  `,
  styleUrls: ['./color-picker-result.component.scss'],
})
export class ColorPickerResultComponent {
  readonly original = input<string | null>(null);
  readonly result = input<string | null>(null);
  readonly originalLabel = input('');
  readonly resultLabel = input('');
  readonly steps = input<readonly ColorPickerResultStep[]>([]);
}
