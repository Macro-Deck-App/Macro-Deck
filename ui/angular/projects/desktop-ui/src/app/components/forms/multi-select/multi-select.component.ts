import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
  signal,
} from '@angular/core';

import { FormsModule } from '@angular/forms';

import { Strings } from '@macro-deck/runtime';
import { CheckboxComponent, LocalizationService, OverlayPanelComponent, TranslatePipe } from '@shared';
import { SelectCaretComponent } from '../select-caret/select-caret.component';

export interface MultiSelectOption {
  value: string;
  label?: string;
}

@Component({
  selector: 'shared-multi-select',
  standalone: true,
  imports: [FormsModule, OverlayPanelComponent, CheckboxComponent, SelectCaretComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button #trigger type="button" class="ms-trigger" (click)="toggle()">
      @if (value.length === 0) {
        <span class="ms-placeholder">{{ placeholder }}</span>
      } @else {
        <span class="ms-summary">{{ summary() }}</span>
      }
      <shared-select-caret />
    </button>

    <shared-overlay-panel
      [anchor]="trigger"
      [isOpen]="isOpen()"
      [matchAnchorWidth]="true"
      [anchorOffset]="0"
      (dismissed)="close()">
      <input
        class="ms-filter"
        type="text"
        [attr.aria-label]="'macrodeck.app:Forms.MultiSelect.FilterOptions' | translate"
        [placeholder]="'macrodeck.app:Forms.MultiSelect.FilterPlaceholder' | translate"
        [ngModel]="filterText()"
        (ngModelChange)="onFilterInput($event)">
      @if (loading) {
        <div class="ms-status">{{ 'macrodeck:Common.Loading' | translate }}</div>
      } @else if (options.length === 0) {
        <div class="ms-status">{{ 'macrodeck.app:Forms.MultiSelect.NoOptions' | translate }}</div>
      } @else if (filteredOptions().length === 0) {
        <div class="ms-status">{{ 'macrodeck.app:Forms.MultiSelect.NoMatches' | translate }}</div>
      } @else {
        @for (option of filteredOptions(); track option.value) {
          <shared-checkbox
            class="ms-option"
            [label]="option.label ?? option.value"
            [ngModel]="isSelected(option.value)"
            (ngModelChange)="toggleOption(option.value)" />
        }
      }
    </shared-overlay-panel>
  `,
  styleUrls: ['./multi-select.component.scss'],
})
export class MultiSelectComponent {
  private readonly localization = inject(LocalizationService);

  private placeholderOverride?: string;

  @Input() value: string[] = [];
  @Input() options: MultiSelectOption[] = [];

  @Input() set placeholder(value: string) {
    this.placeholderOverride = value;
  }
  get placeholder(): string {
    return this.placeholderOverride ?? this.localization.translateKey(Strings.Common.Select);
  }

  @Input() loading = false;

  @Input() filterLocally = true;

  @Output() valueChange = new EventEmitter<string[]>();
  @Output() opened = new EventEmitter<void>();
  @Output() filterChange = new EventEmitter<string>();

  readonly isOpen = signal(false);
  readonly filterText = signal('');

  summary(): string {
    return this.value
      .map(v => this.options.find(o => o.value === v)?.label ?? v)
      .join(', ');
  }

  isSelected(value: string): boolean {
    return this.value.includes(value);
  }

  onFilterInput(filter: string): void {
    this.filterText.set(filter);
    if (!this.filterLocally) {
      this.filterChange.emit(filter);
    }
  }

  filteredOptions(): MultiSelectOption[] {
    const needle = this.filterText().trim().toLowerCase();
    if (!needle || !this.filterLocally) return this.options;
    return this.options.filter(o =>
      (o.label ?? o.value).toLowerCase().includes(needle) || o.value.toLowerCase().includes(needle));
  }

  toggle(): void {
    if (this.isOpen()) {
      this.close();
    } else {
      this.isOpen.set(true);
      this.opened.emit();
    }
  }

  close(): void {
    this.isOpen.set(false);
    this.filterText.set('');
  }

  toggleOption(value: string): void {
    const next = this.isSelected(value)
      ? this.value.filter(v => v !== value)
      : [...this.value, value];
    this.value = next;
    this.valueChange.emit(next);
  }
}
