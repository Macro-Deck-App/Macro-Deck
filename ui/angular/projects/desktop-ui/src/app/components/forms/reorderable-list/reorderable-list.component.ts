import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ButtonComponent, CheckboxComponent, TranslatePipe } from '@shared';
import { MultiSelectOption } from '../multi-select/multi-select.component';

@Component({
  selector: 'shared-reorderable-list',
  standalone: true,
  imports: [FormsModule, CdkDropList, CdkDrag, CdkDragHandle, ButtonComponent, CheckboxComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="rl-list"
      cdkDropList
      cdkDropListLockAxis="y"
      [cdkDropListDisabled]="disabled"
      (cdkDropListDropped)="onDrop($event)">
      @for (item of value; track item; let i = $index) {
        <div class="rl-row" cdkDrag cdkDragPreviewContainer="parent">
          <span
            class="rl-handle"
            cdkDragHandle
            role="img"
            [attr.aria-label]="'macrodeck.app:ActionBuilder.DragToReorder' | translate">
            <svg width="14" height="14" viewBox="0 0 14 14" fill="currentColor" aria-hidden="true">
              <circle cx="4" cy="3" r="1.2"/>
              <circle cx="4" cy="7" r="1.2"/>
              <circle cx="4" cy="11" r="1.2"/>
              <circle cx="10" cy="3" r="1.2"/>
              <circle cx="10" cy="7" r="1.2"/>
              <circle cx="10" cy="11" r="1.2"/>
            </svg>
          </span>
          <shared-checkbox
            class="rl-check"
            [label]="labelOf(item)"
            [disabled]="disabled"
            [ngModel]="true"
            (ngModelChange)="toggle(item)" />
          <shared-button class="rl-up" variant="ghost" size="icon-compact"
            [ariaLabel]="'macrodeck.app:Forms.KeyboardSequenceEditor.MoveUp' | translate"
            [disabled]="disabled || i === 0" (click)="move(i, -1)">
            <span class="icon icon-arrow-up icon-sm" aria-hidden="true"></span>
          </shared-button>
          <shared-button class="rl-down" variant="ghost" size="icon-compact"
            [ariaLabel]="'macrodeck.app:Forms.KeyboardSequenceEditor.MoveDown' | translate"
            [disabled]="disabled || i === value.length - 1" (click)="move(i, 1)">
            <span class="icon icon-arrow-down icon-sm" aria-hidden="true"></span>
          </shared-button>
        </div>
      }
    </div>
    @for (option of unselected(); track option.value) {
      <div class="rl-row">
        <span class="rl-handle-space" aria-hidden="true"></span>
        <shared-checkbox
          class="rl-check"
          [label]="option.label ?? option.value"
          [disabled]="disabled"
          [ngModel]="false"
          (ngModelChange)="toggle(option.value)" />
      </div>
    }
    @if (loading) {
      <div class="rl-status">{{ 'macrodeck:Common.Loading' | translate }}</div>
    } @else if (value.length === 0 && options.length === 0) {
      <div class="rl-status">{{ 'macrodeck.app:Forms.MultiSelect.NoOptions' | translate }}</div>
    }
  `,
  styles: [`
    :host { display: flex; flex-direction: column; gap: var(--space-1); flex: 1 1 0; min-width: 6.5rem; }
    .rl-list { display: flex; flex-direction: column; gap: var(--space-1); }
    .rl-row {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      padding: 0.125rem 0.25rem;
      border-radius: var(--radius-sm);
      background-color: var(--color-bg-surface, transparent);
      overflow-wrap: anywhere;
    }
    .rl-handle, .rl-handle-space { flex: 0 0 14px; display: flex; color: var(--color-text-muted); }
    .rl-handle { cursor: grab; touch-action: none; }
    .rl-check { flex: 1 1 auto; min-width: 0; }
    .rl-status { padding: var(--space-2) 0.5rem; color: var(--color-text-muted); font-size: var(--text-xs); }
    .cdk-drag-preview { box-shadow: var(--shadow-md); background-color: var(--color-bg-elevated); }
    .cdk-drag-placeholder { opacity: 0.4; }
    .cdk-drag-animating, .rl-list.cdk-drop-list-dragging .rl-row:not(.cdk-drag-placeholder) {
      transition: transform 150ms ease;
    }
  `],
})
export class ReorderableListComponent {
  @Input() value: string[] = [];
  @Input() options: MultiSelectOption[] = [];
  @Input() loading = false;
  @Input() disabled = false;

  @Output() valueChange = new EventEmitter<string[]>();

  labelOf(value: string): string {
    return this.options.find(o => o.value === value)?.label ?? value;
  }

  unselected(): MultiSelectOption[] {
    return this.options.filter(o => !this.value.includes(o.value));
  }

  toggle(value: string): void {
    this.emit(this.value.includes(value) ? this.value.filter(v => v !== value) : [...this.value, value]);
  }

  move(index: number, delta: number): void {
    this.reorder(index, index + delta);
  }

  onDrop(event: CdkDragDrop<unknown>): void {
    this.reorder(event.previousIndex, event.currentIndex);
  }

  private reorder(from: number, to: number): void {
    if (from === to || to < 0 || to >= this.value.length) return;
    const next = [...this.value];
    moveItemInArray(next, from, to);
    this.emit(next);
  }

  private emit(next: string[]): void {
    this.value = next;
    this.valueChange.emit(next);
  }
}
