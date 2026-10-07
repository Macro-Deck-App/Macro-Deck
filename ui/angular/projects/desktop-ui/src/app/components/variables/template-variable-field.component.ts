import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ViewContainerRef,
  inject,
  input,
  inputBinding,
  output,
  outputBinding,
  viewChild,
} from '@angular/core';
import type { Variable, VariableScope, VariableType } from '@macro-deck/runtime';
import type { TemplateVariablePreviewOptions } from '../../domain/template-preview.interface';

@Component({
  selector: 'shared-template-variable-field',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ng-container #field />`,
  styles: [':host { display: block; min-width: 0; }'],
})
export class TemplateVariableFieldComponent implements AfterViewInit {
  readonly value = input('');
  readonly variables = input<Variable[]>([]);
  readonly scope = input<VariableScope>('global');
  readonly scopeRefId = input<string | undefined>(undefined);
  readonly resultType = input<VariableType | undefined>(undefined);
  readonly resultOptions = input<Omit<TemplateVariablePreviewOptions, 'resultType'> | undefined>(undefined);
  readonly ariaLabel = input<string | null>(null);

  readonly valueChange = output<string>();

  private readonly field = viewChild.required('field', { read: ViewContainerRef });
  private destroyed = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.destroyed = true);
  }

  ngAfterViewInit(): void {
    // Loaded at runtime: the template editor's variable browser embeds the variables manager, which
    // renders this field, so a static import of the param input would be circular.
    void import('../forms/param-input/param-input.component').then(({ ParamInputComponent }) => {
      if (this.destroyed) return;
      this.field().createComponent(ParamInputComponent, {
        bindings: [
          inputBinding('value', this.value),
          inputBinding('variables', this.variables),
          inputBinding('scope', this.scope),
          inputBinding('scopeRefId', this.scopeRefId),
          inputBinding('resultType', this.resultType),
          inputBinding('resultOptions', this.resultOptions),
          inputBinding('ariaLabel', this.ariaLabel),
          outputBinding<string>('valueChange', value => this.valueChange.emit(value)),
        ],
      });
    });
  }
}
