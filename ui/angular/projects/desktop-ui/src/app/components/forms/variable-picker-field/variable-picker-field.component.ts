import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import type { VariableType } from '@macro-deck/runtime';
import { ButtonComponent, TranslatePipe, VariableService } from '@shared';
import { VariablePickerComponent } from '../../variable-picker/variable-picker.component';

@Component({
  selector: 'shared-node-variable-picker',
  standalone: true,
  imports: [VariablePickerComponent, ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (value(); as name) {
      <span class="config-node-chip">
        <span class="config-node-chip-text">{{ name }}</span>
        <shared-button variant="ghost" size="compact" (click)="valueChange.emit('')">{{
          'macrodeck:Common.Remove' | translate
        }}</shared-button>
      </span>
    } @else {
      <shared-variable-picker
        [variables]="pickerVariables()"
        [acceptedTypes]="variableTypes()"
        (pick)="valueChange.emit($event)" />
    }
  `,
  styles: `
    .config-node-chip {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      padding: var(--space-1) var(--space-2);
      background-color: var(--color-bg-hover);
      border-radius: var(--radius-sm);
      color: var(--color-text-primary);
      font-size: var(--text-sm);
    }
  `,
})
export class NodeVariablePickerComponent {
  private readonly variableService = inject(VariableService);

  readonly value = input('');
  readonly variableTypes = input<VariableType[] | undefined>(undefined);
  readonly writableOnly = input(false);

  readonly valueChange = output<string>();

  protected readonly pickerVariables = computed(() => {
    const all = this.variableService.variables();
    return this.writableOnly() ? all.filter(variable => variable.canWrite === true) : all;
  });
}
