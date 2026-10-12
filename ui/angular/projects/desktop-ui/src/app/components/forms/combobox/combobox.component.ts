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
import { FontPreviewDirective } from './font-preview.directive';

let nextComboboxId = 0;

export interface ComboboxOption {
  value: string;
  label?: string;
  disabled?: boolean;
  metadata?: Record<string, string>;
  keywords?: string[];
  fontFaceId?: string;
}

@Component({
  selector: 'shared-combobox',
  standalone: true,
  imports: [FontPreviewDirective, FormsModule, OverlayPanelComponent, SelectCaretComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <input
      #input
      class="cb-input"
      type="text"
      [placeholder]="placeholder"
      [ngModel]="shownText()"
      [disabled]="disabled"
      [attr.role]="pickOnly ? 'combobox' : null"
      [attr.aria-autocomplete]="pickOnly ? 'list' : null"
      [attr.aria-expanded]="pickOnly ? isOpen() : null"
      [attr.aria-controls]="pickOnly ? listboxId : null"
      [attr.aria-activedescendant]="pickOnly ? activeOptionId() : null"
      (ngModelChange)="onInput($event)"
      (keydown)="onKeydown($event)"
      (focus)="startEdit()"
      (blur)="stopEdit()">

    <!-- Not a dead zone: it sits over the input, which cannot contain children, so it needs its own
         click handler to do what focusing the input already does - open the suggestion list. -->
    <span class="cb-caret" (mousedown)="$event.preventDefault()" (click)="toggleFromCaret()">
      <shared-select-caret />
    </span>

    @if (showClear && !disabled) {
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
        <div class="cb-listbox" [id]="listboxId" [attr.role]="pickOnly ? 'listbox' : null">
          @for (option of filteredOptions(); track option.value; let i = $index) {
            <button
              type="button"
              class="cb-option"
              [id]="optionId(i)"
              [attr.role]="pickOnly ? 'option' : null"
              [attr.aria-selected]="pickOnly ? option.value === value : null"
              [class.cb-option-active]="option.value === value"
              [class.cb-option-highlight]="i === activeIndex()"
              [disabled]="option.disabled"
              (mousedown)="$event.preventDefault()"
              (click)="pick(option)">
              @if (option.fontFaceId) {
                <span [sharedFontPreview]="option.fontFaceId">{{ option.label ?? option.value }}</span>
              } @else {
                {{ option.label ?? option.value }}
              }
            </button>
          }
        </div>
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
  @Input() pickOnly = false;
  @Input() disabled = false;

  @Output() valueChange = new EventEmitter<string>();
  @Output() filterChange = new EventEmitter<string>();
  @Output() opened = new EventEmitter<void>();

  @ViewChild('input') private inputRef?: ElementRef<HTMLInputElement>;

  readonly isOpen = signal(false);
  readonly activeIndex = signal(-1);
  private readonly idPrefix = `shared-combobox-${nextComboboxId++}`;
  readonly listboxId = `${this.idPrefix}-listbox`;
  readonly editing = signal(false);
  private readonly query = signal('');
  private pickerMode = false;

  get showClear(): boolean {
    return this.clearable && !!this.value;
  }

  shownText(): string {
    if (this.editing()) return this.query();
    return this.displayLabel || this.pickedOptionLabel() || this.value;
  }

  private pickedOptionLabel(): string {
    if (!this.pickOnly) return '';
    return this.options.find(option => option.value === this.value)?.label ?? '';
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
      || (!this.pickOnly && o.value.toLowerCase().includes(needle))
      || (o.keywords?.some(k => k.toLowerCase().replace(/\s+/g, '').includes(packed)) ?? false));
  }

  onInput(value: string): void {
    this.editing.set(true);
    this.query.set(value);
    this.isOpen.set(true);
    this.filterChange.emit(value);
    if (this.pickOnly) this.activeIndex.set(this.stepFrom(-1, 1));
    if (!this.pickerMode) {
      this.value = value;
      this.valueChange.emit(value);
    }
  }

  startEdit(): void {
    this.pickerMode = this.pickOnly || !!this.displayLabel;
    this.query.set(this.pickerMode ? '' : this.value);
    this.editing.set(true);
    this.activeIndex.set(this.pickOnly ? this.options.findIndex(option => option.value === this.value) : -1);
    this.open();
  }

  optionId(index: number): string {
    return `${this.idPrefix}-option-${index}`;
  }

  activeOptionId(): string | null {
    const index = this.activeIndex();
    return this.isOpen() && index >= 0 && index < this.filteredOptions().length ? this.optionId(index) : null;
  }

  onKeydown(event: KeyboardEvent): void {
    if (!this.pickOnly || this.disabled) return;
    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp':
        event.preventDefault();
        this.open();
        this.activeIndex.set(this.stepFrom(this.activeIndex(), event.key === 'ArrowDown' ? 1 : -1));
        return;
      case 'Enter': {
        const option = this.filteredOptions()[this.activeIndex()];
        if (!this.isOpen() || !option || option.disabled) return;
        event.preventDefault();
        this.pick(option);
        return;
      }
      case 'Escape':
        if (!this.isOpen()) return;
        event.preventDefault();
        this.close();
        return;
    }
  }

  private stepFrom(start: number, step: number): number {
    const options = this.filteredOptions();
    let index = start;
    for (let tried = 0; tried < options.length; tried++) {
      index = index < 0 ? (step > 0 ? 0 : options.length - 1) : (index + step + options.length) % options.length;
      if (!options[index].disabled) return index;
    }
    return -1;
  }

  stopEdit(): void {
    this.editing.set(false);
  }

  open(): void {
    if (this.disabled) return;
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
    this.activeIndex.set(-1);
  }

  clear(): void {
    this.value = '';
    this.query.set('');
    this.pickerMode = this.pickOnly;
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
