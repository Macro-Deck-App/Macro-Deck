import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import type { VariableType } from '@macro-deck/runtime';
import { ButtonComponent, TranslatePipe, VariableService } from '@shared';
import { VariablePickerComponent } from '../../variable-picker/variable-picker.component';
import { UiRenderContext } from '../../ui-render/ui-render-context';

@Component({
  selector: 'shared-node-variable-picker',
  standalone: true,
  imports: [VariablePickerComponent, ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (value(); as name) {
      <span class="config-node-chip">
        <span class="config-node-chip-text" [title]="name">{{ name }}</span>
        <shared-button variant="ghost" size="compact" (click)="valueChange.emit('')">{{
          'macrodeck:Common.Remove' | translate
        }}</shared-button>
      </span>
    } @else {
      <shared-variable-picker
        [variables]="pickerVariables()"
        [acceptedTypes]="variableTypes()"
        [writableOnly]="writableOnly()"
        (pick)="valueChange.emit($event)" />
    }
  `,
  styles: `
    @use '../../../../styles/index' as ds;

    .config-node-chip {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      max-width: 100%;
      min-width: 0;
      padding: var(--space-1) var(--space-2);
      background-color: var(--color-bg-hover);
      border-radius: var(--radius-sm);
      color: var(--color-text-primary);
      font-size: var(--text-sm);
    }

    .config-node-chip-text {
      @include ds.truncate;
      min-width: 0;
    }

    .config-node-chip > shared-button {
      flex: 0 0 auto;
    }
  `,
})
export class NodeVariablePickerComponent {
  private readonly variableService = inject(VariableService);
  private readonly context = inject(UiRenderContext);

  readonly value = input('');
  readonly variableTypes = input<VariableType[] | undefined>(undefined);
  readonly writableOnly = input(false);

  readonly valueChange = output<string>();

  protected readonly pickerVariables = computed(() => {
    const scopeRefId = this.context.scopeRefId;
    const all = scopeRefId
      ? this.variableService.visibleForContext('widget', scopeRefId)
      : this.variableService.variables()
        .filter(variable => variable.scope !== 'script' && variable.scope !== 'automation');
    return this.writableOnly() ? all.filter(variable => variable.canWrite === true) : all;
  });
}
