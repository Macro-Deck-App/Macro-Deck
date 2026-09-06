import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';

@Component({
  selector: 'shared-duration-input',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <input
      class="du-input"
      type="text"
      [class.du-invalid]="invalid"
      [placeholder]="placeholder"
      [ngModel]="text"
      (ngModelChange)="onInput($event)"
      (blur)="onBlur()">
  `,
  styleUrls: ['./duration-input.component.scss'],
})
export class DurationInputComponent {
  private readonly localization = inject(LocalizationService);

  private placeholderOverride?: string;

  @Input() set placeholder(value: string) {
    this.placeholderOverride = value;
  }
  get placeholder(): string {
    return this.placeholderOverride ?? this.localization.translateKey(AppStrings.Forms.DurationInput.Placeholder);
  }

  @Input()
  set value(milliseconds: number | null) {
    if (milliseconds !== this.lastEmitted) {
      this.text = DurationInputComponent.format(milliseconds ?? 0);
    }
  }

  @Output() valueChange = new EventEmitter<number>();

  text = '';
  invalid = false;
  private lastEmitted: number | null = null;

  onInput(text: string): void {
    this.text = text;
    const parsed = DurationInputComponent.parse(text);
    this.invalid = parsed === null && text.trim() !== '';
    if (parsed !== null) {
      this.lastEmitted = parsed;
      this.valueChange.emit(parsed);
    }
  }

  onBlur(): void {
    const parsed = DurationInputComponent.parse(this.text);
    if (parsed !== null) {
      this.text = DurationInputComponent.format(parsed);
      this.invalid = false;
    }
  }

  static parse(text: string): number | null {
    const trimmed = text.trim().toLowerCase();
    if (trimmed === '') return null;

    const match = /^(\d+(?:\.\d+)?)\s*(ms|s|m|h)?$/.exec(trimmed);
    if (!match) return null;

    const amount = parseFloat(match[1]);
    const factor = match[2] === 's' ? 1_000
      : match[2] === 'm' ? 60_000
      : match[2] === 'h' ? 3_600_000
      : 1;
    return amount * factor;
  }

  static format(milliseconds: number): string {
    if (milliseconds === 0) return '0ms';
    if (milliseconds % 3_600_000 === 0) return `${milliseconds / 3_600_000}h`;
    if (milliseconds % 60_000 === 0) return `${milliseconds / 60_000}m`;
    if (milliseconds % 1_000 === 0) return `${milliseconds / 1_000}s`;
    return `${milliseconds}ms`;
  }
}
