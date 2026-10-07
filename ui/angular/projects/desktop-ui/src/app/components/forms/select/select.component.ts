import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  forwardRef,
  inject,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

import { Strings } from '@macro-deck/runtime';
import { LocalizationService, OverlayPanelComponent, TranslatePipe } from '@shared';
import { SelectCaretComponent } from '../select-caret/select-caret.component';

export interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
  group?: string;
  badge?: string;
  icon?: string;
  swatch?: string | null;
}

const TYPEAHEAD_RESET_MS = 500;

let nextSelectId = 0;

@Component({
  selector: 'shared-select',
  standalone: true,
  imports: [OverlayPanelComponent, SelectCaretComponent, TranslatePipe, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => SelectComponent),
    multi: true,
  }],
  template: `
    <button
      #trigger
      type="button"
      class="control"
      role="combobox"
      aria-haspopup="listbox"
      [attr.aria-expanded]="isOpen()"
      [attr.title]="showTitle ? selectedLabel() : null"
      [disabled]="disabled"
      (click)="toggle()"
      (keydown)="onTriggerKeydown($event)"
      (blur)="onTouched()">
      @if (selectedOption(); as selected) {
        <ng-container [ngTemplateOutlet]="leading" [ngTemplateOutletContext]="{ $implicit: selected }" />
      }
      <span class="sel-label" [class.sel-placeholder]="selectedLabel() === null">
        {{ selectedLabel() ?? placeholder }}
      </span>
      <shared-select-caret />
    </button>

    @if (showClear) {
      <!-- A sibling, not a child: the trigger is itself a button, and nesting one inside it is
           invalid markup that swallows the inner click in some browsers. -->
      <button
        type="button"
        class="sel-clear"
        [attr.aria-label]="'macrodeck.app:Forms.Select.ClearSelection' | translate"
        (mousedown)="$event.preventDefault()"
        (click)="clear()">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
          stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
          <path d="M18 6 6 18"/>
          <path d="m6 6 12 12"/>
        </svg>
      </button>
    }

    <ng-template #leading let-option>
      @if (option.swatch !== undefined) {
        <span
          class="sel-swatch"
          aria-hidden="true"
          [class.sel-swatch-empty]="!option.swatch"
          [style.--sel-swatch]="option.swatch || null"></span>
      } @else if (option.icon) {
        <span class="icon icon-sm icon-{{ option.icon }} sel-option-icon" aria-hidden="true"></span>
      }
    </ng-template>

    <shared-overlay-panel
      [anchor]="trigger"
      [isOpen]="isOpen()"
      [matchAnchorWidth]="!fitContent"
      [minWidth]="fitContent ? panelMinWidth() : null"
      [anchorOffset]="0"
      (dismissed)="close()">
      @if (searchable) {
        <div class="sel-search">
          <span class="icon icon-xs icon-search sel-search-icon" aria-hidden="true"></span>
          <input
            #search
            class="control sel-search-input"
            type="text"
            role="combobox"
            aria-autocomplete="list"
            autocomplete="off"
            spellcheck="false"
            [attr.aria-label]="searchPlaceholder || null"
            [attr.aria-controls]="listboxId"
            [attr.aria-expanded]="true"
            [attr.aria-activedescendant]="activeOptionId()"
            [placeholder]="searchPlaceholder"
            [value]="query()"
            (input)="onSearchInput($event)"
            (keydown)="onSearchKeydown($event)">
        </div>
      }
      <div class="sel-listbox" role="listbox" [id]="listboxId" [class.sel-listbox-fit]="fitContent">
        @for (option of visibleOptions; track option.value; let i = $index) {
          @if (option.group && (i === 0 || visibleOptions[i - 1].group !== option.group)) {
            <div class="sel-group">{{ option.group }}</div>
          }
          <button
            type="button"
            class="sel-option"
            role="option"
            [id]="optionId(i)"
            [class.sel-option-selected]="isSelected(option)"
            [class.sel-option-active]="i === activeIndex()"
            [attr.aria-selected]="isSelected(option)"
            [disabled]="option.disabled"
            (mousedown)="$event.preventDefault()"
            (click)="pick(option)">
            <ng-container [ngTemplateOutlet]="leading" [ngTemplateOutletContext]="{ $implicit: option }" />
            <span class="sel-option-label">{{ option.label }}</span>
            @if (option.badge) {
              <span class="sel-option-badge">{{ option.badge }}</span>
            }
          </button>
        }
        @if (visibleOptions.length === 0) {
          @if (options.length > 0) {
            <div class="sel-empty">{{ noMatchesText }}</div>
          } @else if (emptyText) {
            <div class="sel-empty">{{ emptyText }}</div>
          } @else {
            <div class="sel-empty">{{ 'macrodeck.app:Forms.Select.NoOptions' | translate }}</div>
          }
        }
      </div>
    </shared-overlay-panel>
  `,
  styleUrls: ['./select.component.scss'],
})
export class SelectComponent implements ControlValueAccessor {
  private readonly localization = inject(LocalizationService);

  private placeholderOverride?: string;

  @Input() disabled = false;
  @Input() options: SelectOption[] = [];

  @Input() set placeholder(value: string) {
    this.placeholderOverride = value;
  }
  get placeholder(): string {
    return this.placeholderOverride ?? this.localization.translateKey(Strings.Common.Select);
  }
  @Input() clearable = false;
  @Input() searchable = false;
  @Input() searchPlaceholder = '';
  @Input() noMatchesText = '';
  @Input() emptyText = '';
  @Input() fitContent = false;
  @Input() showTitle = false;

  @Output() readonly opened = new EventEmitter<void>();

  @ViewChild('trigger') private triggerRef?: ElementRef<HTMLButtonElement>;
  @ViewChild('search') private searchRef?: ElementRef<HTMLInputElement>;

  private readonly idPrefix = `sel-${++nextSelectId}`;
  readonly listboxId = `${this.idPrefix}-listbox`;
  readonly query = signal('');
  readonly panelMinWidth = signal<number | null>(null);

  private readonly valueSignal = signal<string | number | null>(null);
  readonly isOpen = signal(false);
  readonly activeIndex = signal(-1);

  private typeaheadBuffer = '';
  private typeaheadAt = 0;

  onChange: (value: string | number | null) => void = () => {};
  onTouched: () => void = () => {};

  get value(): string | number | null {
    return this.valueSignal();
  }

  get showClear(): boolean {
    const value = this.valueSignal();
    return this.clearable && !this.disabled && value !== null && value !== '';
  }

  clear(): void {
    this.valueSignal.set('');
    this.onChange('');
    this.isOpen.set(false);
  }

  get visibleOptions(): SelectOption[] {
    const query = this.searchable ? this.query().trim().toLowerCase() : '';
    return query === '' ? this.options : this.options.filter(option => option.label.toLowerCase().includes(query));
  }

  selectedOption(): SelectOption | null {
    const value = this.valueSignal();
    return value === null ? null : this.options.find(o => o.value === String(value)) ?? null;
  }

  activeOptionId(): string | null {
    const index = this.activeIndex();
    return index >= 0 && index < this.visibleOptions.length ? this.optionId(index) : null;
  }

  selectedLabel(): string | null {
    const value = this.valueSignal();
    if (value === null || value === '') {
      const empty = this.options.find(o => o.value === '');
      return empty?.label ?? null;
    }
    return this.options.find(o => o.value === String(value))?.label ?? String(value);
  }

  isSelected(option: SelectOption): boolean {
    const value = this.valueSignal();
    return value !== null && String(value) === option.value;
  }

  optionId(index: number): string {
    return `${this.idPrefix}-option-${index}`;
  }

  writeValue(value: string | number | null): void {
    this.valueSignal.set(value ?? null);
  }

  registerOnChange(fn: (value: string | number | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabled = disabled;
  }

  toggle(): void {
    if (this.isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }

  open(): void {
    if (this.disabled) {
      return;
    }
    this.query.set('');
    const value = this.valueSignal();
    const selected = this.options.findIndex(o => value !== null && String(value) === o.value);
    this.activeIndex.set(selected >= 0 ? selected : this.firstEnabled(0, 1));
    this.typeaheadBuffer = '';
    if (this.fitContent) {
      this.panelMinWidth.set(this.triggerRef?.nativeElement.getBoundingClientRect().width || null);
    }
    this.isOpen.set(true);
    if (this.searchable) {
      requestAnimationFrame(() => this.searchRef?.nativeElement.focus());
    }
    if (this.activeIndex() >= 0) {
      this.scrollActiveIntoView(this.activeIndex());
    }
    this.opened.emit();
  }

  close(): void {
    this.typeaheadBuffer = '';
    this.isOpen.set(false);
    this.triggerRef?.nativeElement.focus();
  }

  pick(option: SelectOption): void {
    if (option.disabled) {
      return;
    }
    this.valueSignal.set(option.value);
    this.onChange(option.value);
    this.close();
  }

  onSearchInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
    this.activeIndex.set(this.firstEnabled(0, 1));
  }

  onSearchKeydown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.moveActive(1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.moveActive(-1);
        break;
      case 'Enter': {
        event.preventDefault();
        const active = this.visibleOptions[this.activeIndex()];
        if (active) {
          this.pick(active);
        }
        break;
      }
      case 'Tab':
        this.close();
        break;
    }
  }

  onTriggerKeydown(event: KeyboardEvent): void {
    if (!this.isOpen()) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        this.open();
      } else if (this.isTypeaheadKey(event)) {
        event.preventDefault();
        this.open();
        this.typeahead(event.key);
      }
      return;
    }
    if (event.key === ' ' && this.searchInProgress()) {
      event.preventDefault();
      this.typeahead(event.key);
      return;
    }
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.moveActive(1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.moveActive(-1);
        break;
      case 'Home':
        event.preventDefault();
        this.activeIndex.set(this.firstEnabled(0, 1));
        break;
      case 'End':
        event.preventDefault();
        this.activeIndex.set(this.firstEnabled(this.visibleOptions.length - 1, -1));
        break;
      case 'Enter':
      case ' ': {
        event.preventDefault();
        const active = this.visibleOptions[this.activeIndex()];
        if (active) {
          this.pick(active);
        }
        break;
      }
      case 'Tab':
        this.close();
        break;
      default:
        if (this.isTypeaheadKey(event)) {
          event.preventDefault();
          this.typeahead(event.key);
        }
        break;
    }
  }

  private isTypeaheadKey(event: KeyboardEvent): boolean {
    return event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey;
  }

  private searchInProgress(): boolean {
    return this.typeaheadBuffer !== '' && Date.now() - this.typeaheadAt <= TYPEAHEAD_RESET_MS;
  }

  private typeahead(key: string): void {
    this.typeaheadBuffer = this.searchInProgress() ? this.typeaheadBuffer + key : key;
    this.typeaheadAt = Date.now();

    const buffer = this.typeaheadBuffer;
    const repeated = [...buffer].every(character => character === buffer[0]);
    const prefix = (repeated ? buffer[0] : buffer).toLowerCase();

    const match = this.findByPrefix(prefix, repeated);
    if (match < 0) {
      return;
    }
    this.activeIndex.set(match);
    this.scrollActiveIntoView(match);
  }

  private findByPrefix(prefix: string, fromNext: boolean): number {
    const count = this.visibleOptions.length;
    if (count === 0) {
      return -1;
    }
    const start = this.activeIndex() + (fromNext ? 1 : 0);
    for (let step = 0; step < count; step++) {
      const index = ((start + step) % count + count) % count;
      const option = this.visibleOptions[index];
      if (!option.disabled && option.label.toLowerCase().startsWith(prefix)) {
        return index;
      }
    }
    return -1;
  }

  private moveActive(step: number): void {
    const start = this.activeIndex();
    let index = start;
    do {
      index += step;
      if (index < 0 || index >= this.visibleOptions.length) {
        return;
      }
    } while (this.visibleOptions[index]?.disabled);
    this.activeIndex.set(index);
    this.scrollActiveIntoView(index);
  }

  private firstEnabled(start: number, step: number): number {
    let index = start;
    while (index >= 0 && index < this.visibleOptions.length) {
      if (!this.visibleOptions[index].disabled) {
        return index;
      }
      index += step;
    }
    return -1;
  }

  private scrollActiveIntoView(index: number): void {
    requestAnimationFrame(() => {
      document.getElementById(this.optionId(index))?.scrollIntoView({ block: 'nearest' });
    });
  }
}
