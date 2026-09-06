import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';

import { ActionBlock, ActionBlockParameter, AppStrings, ParameterValue, ScriptInput, ScriptInputValue, scriptInputDefaultValue, scriptInputParameter } from '@macro-deck/runtime';
import { ButtonComponent, ButtonGroupComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';
import { ParamRowComponent } from '../../action-builder/action-card/param-list/param-row.component';
import { ActionFlowStore } from '../../action-builder/services/action-flow.store';

@Component({
  selector: 'app-script-run-dialog',
  standalone: true,
  imports: [ButtonComponent, ButtonGroupComponent, ModalComponent, ParamRowComponent, TranslatePipe],
  providers: [ActionFlowStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './script-run-dialog.component.html',
  styleUrls: ['./script-run-dialog.component.scss'],
})
export class ScriptRunDialogComponent implements OnInit {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true }) scriptName = '';
  @Input({ required: true }) inputs: ScriptInput[] = [];

  @Output() run = new EventEmitter<Record<string, ScriptInputValue>>();
  @Output() cancel = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly heading = computed(() =>
    this.localization.translateKey(AppStrings.Scripts.RunScriptHeading, { name: this.scriptName }));

  private readonly values = signal<Record<string, ParameterValue>>({});

  private readonly touched = signal<ReadonlySet<string>>(new Set<string>());

  protected readonly rows = computed<ActionBlockParameter[]>(() => {
    const values = this.values();
    return this.inputs.map(input => scriptInputParameter(input, values[input.name] ?? ''));
  });

  protected readonly block = computed<ActionBlock>(() => ({
    id: 'script-run',
    type: 'action',
    blockType: 'script-run',
    label: this.scriptName,
    color: 'var(--color-accent)',
    parameters: this.rows(),
  }));

  protected readonly canRun = computed(() =>
    this.inputs.every(input => !this.isMissing(input, this.values()[input.name])),
  );

  ngOnInit(): void {
    const seeded: Record<string, ParameterValue> = {};
    for (const input of this.inputs) {
      seeded[input.name] = scriptInputDefaultValue(input);
    }
    this.values.set(seeded);
  }

  protected setValue(name: string, value: ParameterValue): void {
    this.values.update(current => ({ ...current, [name]: value }));
    this.touched.update(current => new Set(current).add(name));
  }

  protected submit(): void {
    if (!this.canRun()) return;
    const values = this.values();
    const touched = this.touched();
    const supplied: Record<string, ScriptInputValue> = {};
    for (const input of this.inputs) {
      const value = values[input.name];
      if (!this.isSupplied(input, value, touched)) continue;
      supplied[input.name] = ScriptRunDialogComponent.toWireValue(input, value);
    }
    dismissModal(this.modal, () => this.run.emit(supplied));
  }

  protected onCancel(): void {
    dismissModal(this.modal, () => this.cancel.emit());
  }

  private isSupplied(
    input: ScriptInput,
    value: ParameterValue | undefined,
    touched: ReadonlySet<string>,
  ): boolean {
    if (value === null || value === undefined) return false;
    // An emptied text box is an answer - the empty string - but an emptied number box is not.
    if (value === '' && input.type !== 'text') return false;
    // Nothing can be left to fall back to for a required input without a default, so what the
    // dialog shows for it is the answer even if the user never touched the control.
    return touched.has(input.name) || (input.required === true && input.defaultValue === undefined);
  }

  private isMissing(input: ScriptInput, value: ParameterValue | undefined): boolean {
    if (input.required !== true || input.defaultValue !== undefined) return false;
    if (input.type === 'boolean') return false;
    return value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
  }

  private static toWireValue(input: ScriptInput, value: ParameterValue | undefined): ScriptInputValue {
    switch (input.type) {
      case 'boolean':
        return value === true || value === 'true';
      case 'numeric': {
        const numeric = typeof value === 'number' ? value : Number(value);
        return Number.isFinite(numeric) ? numeric : null;
      }
      default:
        return value === null || value === undefined ? '' : String(value);
    }
  }
}
