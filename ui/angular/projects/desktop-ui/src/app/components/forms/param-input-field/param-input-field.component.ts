import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import type { Variable, VariableScope } from '@macro-deck/runtime';
import { VariableService } from '@shared';
import { ParamInputComponent } from '../param-input/param-input.component';
import { UiRenderContext } from '../../ui-render/ui-render-context';

@Component({
  selector: 'shared-node-param-input',
  standalone: true,
  imports: [ParamInputComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-param-input
      [value]="value()"
      [placeholder]="placeholder() ?? ''"
      [multiline]="multiline()"
      [ariaLabel]="ariaLabel()"
      [variables]="variables()"
      [scope]="scope()"
      [scopeRefId]="scopeRefId()"
      (valueChange)="valueChange.emit($event)" />
  `,
})
export class NodeParamInputComponent {
  private readonly variableService = inject(VariableService);
  private readonly context = inject(UiRenderContext);

  readonly value = input('');
  readonly placeholder = input<string | null>(null);
  readonly multiline = input(false);
  readonly ariaLabel = input<string | null>(null);

  readonly valueChange = output<string>();

  protected readonly scopeRefId = computed(() => this.context.scopeRefId);
  protected readonly scope = computed<VariableScope>(() => (this.scopeRefId() ? 'widget' : 'global'));
  protected readonly variables = computed<Variable[]>(
    () => this.variableService.visibleForContext(this.scope(), this.scopeRefId()),
  );
}
