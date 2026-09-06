import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { OverlayPanelComponent, TranslatePipe } from '@shared';
import { SelectCaretComponent } from '../select-caret/select-caret.component';

export interface ComboboxOption {
  value: string;
  label?: string;
  disabled?: boolean;
  metadata?: Record<string, string>;
  keywords?: string[];
}

@Component({
  selector: 'shared-combobox',
  standalone: true,
  imports: [FormsModule, OverlayPanelComponent, SelectCaretComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <input
      #input
      class="cb-input"
      type="text"
      [placeholder]="placeholder"
      [ngModel]="shownText()"
      (ngModelChange)="onInput($event)"
      (focus)="startEdit()"
      (blur)="stopEdit()">

    <!-- Not a dead zone: it sits over the input, which cannot contain children, so it needs its own
         click handler to do what focusing the input already does - open the suggestion list. -->
    <span class="cb-caret" (mousedown)="$event.preventDefault()" (click)="toggleFromCaret()">
      <shared-select-caret />
    </span>

    @if (showClear) {
      <button
        type="button"
        class="cb-clear"
        [attr.aria-label]="'macrodeck.app:Forms.Combobox.ClearSelection' | translate"
        (mousedown)="$event.preventDefault()"
        (click)="clear()">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
          stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
          <path d="M18 6 6 18"/>
          <path d="m6 6 12 12"/>
        </svg>
      </button>
    }

    <shared-overlay-panel
      [anchor]="input"
      [isOpen]="isOpen()"
      [matchAnchorWidth]="true"
      [anchorOffset]="0"
      (dismissed)="close()">
      @if (loading) {
        <div class="cb-status">{{ 'macrodeck:Common.Loading' | translate }}</div>
      } @else if (filteredOptions().length === 0) {
        <div class="cb-status">{{ 'macrodeck.app:Forms.Combobox.NoSuggestions' | translate }}</div>
      } @else {
        @for (option of filteredOptions(); track option.value) {
          <button
            type="button"
            class="cb-option"
            [class.cb-option-active]="option.value === value"
            [disabled]="option.disabled"
            (mousedown)="$event.preventDefault()"
            (click)="pick(option)">
            {{ option.label ?? option.value }}
          </button>
        }
      }
    </shared-overlay-panel>
  `,
  styleUrls: ['./combobox.component.scss'],
})
export class ComboboxComponent {
  @Input() value = '';
  @Input() displayLabel = '';
  @Input() placeholder = '';
  @Input() options: ComboboxOption[] = [];
  @Input() loading = false;
  @Input() filterLocally = true;
  @Input() clearable = true;

  @Output() valueChange = new EventEmitter<string>();
  @Output() filterChange = new EventEmitter<string>();
  @Output() opened = new EventEmitter<void>();

  @ViewChild('input') private inputRef?: ElementRef<HTMLInputElement>;

  readonly isOpen = signal(false);
  readonly editing = signal(false);
  private readonly query = signal('');
  private pickerMode = false;

  get showClear(): boolean {
    return this.clearable && !!this.value;
  }

  shownText(): string {
    if (this.editing()) return this.query();
    return this.displayLabel || this.value;
  }

  filteredOptions(): ComboboxOption[] {
    const query = this.query();
    if (!this.filterLocally || !query) return this.options;
    const needle = query.toLowerCase();
    // Keywords are matched with whitespace removed on both sides, so a token like `new york` or
    // `utc+02:00` answers to `newyork` / `utc +2` alike.
    const packed = needle.replace(/\s+/g, '');
    return this.options.filter(o =>
      (o.label ?? o.value).toLowerCase().includes(needle)
      || o.value.toLowerCase().includes(needle)
      || (o.keywords?.some(k => k.toLowerCase().replace(/\s+/g, '').includes(packed)) ?? false));
  }

  onInput(value: string): void {
    this.editing.set(true);
    this.query.set(value);
    this.isOpen.set(true);
    this.filterChange.emit(value);
    if (!this.pickerMode) {
      this.value = value;
      this.valueChange.emit(value);
    }
  }

  startEdit(): void {
    this.pickerMode = !!this.displayLabel;
    this.query.set(this.pickerMode ? '' : this.value);
    this.editing.set(true);
    this.open();
  }

  stopEdit(): void {
    this.editing.set(false);
  }

  open(): void {
    if (!this.isOpen()) {
      this.isOpen.set(true);
      this.opened.emit();
    }
  }

  toggleFromCaret(): void {
    if (this.isOpen()) {
      this.close();
      return;
    }
    this.inputRef?.nativeElement.focus();
    this.open();
  }

  close(): void {
    this.isOpen.set(false);
  }

  clear(): void {
    this.value = '';
    this.query.set('');
    this.pickerMode = false;
    this.editing.set(false);
    this.close();
    this.valueChange.emit('');
  }

  pick(option: ComboboxOption): void {
    this.value = option.value;
    this.query.set('');
    this.valueChange.emit(option.value);
    this.close();
    this.editing.set(false);
  }
}
