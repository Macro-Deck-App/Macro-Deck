import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
} from '@angular/core';

import { FormsModule } from '@angular/forms';
import { AppStrings, KeyboardComboValue, KeyboardSequenceValue, KeyboardStepType, KeyboardStepValue, formatCombo } from '@macro-deck/runtime';
import { ButtonComponent, InputComponent, LocalizationService, TranslatePipe } from '@shared';
import type { Variable, VariableScope } from '@macro-deck/runtime';
import { KeyboardComboEditorComponent } from '../keyboard-combo-editor/keyboard-combo-editor.component';
import { ParamInputComponent } from '../param-input/param-input.component';
import { SelectComponent, SelectOption } from '../select/select.component';

@Component({
  selector: 'shared-keyboard-sequence-editor',
  standalone: true,
  imports: [
    FormsModule,
    KeyboardComboEditorComponent,
    ParamInputComponent,
    InputComponent,
    SelectComponent,
    ButtonComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './keyboard-sequence-editor.component.html',
  styleUrls: ['./keyboard-sequence-editor.component.scss'],
})
export class KeyboardSequenceEditorComponent {
  private readonly localization = inject(LocalizationService);

  @Input()
  set value(value: KeyboardSequenceValue | null) {
    this.steps = value?.steps ? [...value.steps] : [];
    this.repeat = value?.repeat ?? 1;
    this.repeatDelayMs = value?.repeatDelayMs ?? 0;
  }

  @Output() valueChange = new EventEmitter<KeyboardSequenceValue>();

  @Input() variables: Variable[] = [];
  @Input() scope: VariableScope = 'global';
  @Input() scopeRefId?: string;

  steps: KeyboardStepValue[] = [];
  repeat = 1;
  repeatDelayMs = 0;

  private readonly stepTypeLabels: Record<KeyboardStepType, string> = {
    keyCombo: AppStrings.Forms.KeyboardSequenceEditor.KeyCombination,
    text: AppStrings.Forms.KeyboardSequenceEditor.Text,
    delay: AppStrings.Forms.KeyboardSequenceEditor.Delay,
    keyDown: AppStrings.Forms.KeyboardSequenceEditor.HoldKeyDown,
    keyUp: AppStrings.Forms.KeyboardSequenceEditor.ReleaseKeyUp,
  };

  private readonly stepTypeOrder: KeyboardStepType[] = ['keyCombo', 'text', 'delay', 'keyDown', 'keyUp'];

  readonly stepTypeOptions = computed<SelectOption[]>(() => this.stepTypeOrder.map(type => ({
    value: type,
    label: this.localization.translateKey(this.stepTypeLabels[type]),
  })));

  newStepType: KeyboardStepType = 'keyCombo';

  get preview(): string {
    if (this.steps.length === 0) {
      return this.localization.translateKey(AppStrings.Forms.KeyboardSequenceEditor.EmptySequence);
    }
    return this.steps.map(step => this.describeStep(step)).join('  →  ');
  }

  comboValue(step: KeyboardStepValue): KeyboardComboValue {
    if (step.type === 'keyCombo' || step.type === 'keyDown' || step.type === 'keyUp') {
      return { modifiers: step.modifiers ?? [], key: step.key ?? '' };
    }
    return { modifiers: [], key: '' };
  }

  textValue(step: KeyboardStepValue): string {
    return step.type === 'text' ? step.text : '';
  }

  delayValue(step: KeyboardStepValue): number {
    return step.type === 'delay' ? step.milliseconds : 0;
  }

  comboRepeat(step: KeyboardStepValue): number {
    return step.type === 'keyCombo' ? step.repeat ?? 1 : 1;
  }

  addStep(): void {
    this.steps = [...this.steps, this.createStep(this.newStepType)];
    this.emit();
  }

  removeStep(index: number): void {
    this.steps = this.steps.filter((_, i) => i !== index);
    this.emit();
  }

  moveStep(index: number, delta: number): void {
    const target = index + delta;
    if (target < 0 || target >= this.steps.length) {
      return;
    }
    const next = [...this.steps];
    [next[index], next[target]] = [next[target], next[index]];
    this.steps = next;
    this.emit();
  }

  onComboChange(index: number, combo: KeyboardComboValue): void {
    this.updateStep(index, step => {
      if (step.type === 'keyCombo' || step.type === 'keyDown' || step.type === 'keyUp') {
        return { ...step, modifiers: combo.modifiers, key: combo.key };
      }
      return step;
    });
  }

  onTextChange(index: number, text: string): void {
    this.updateStep(index, step => (step.type === 'text' ? { ...step, text } : step));
  }

  onDelayChange(index: number, value: string | number): void {
    const ms = Math.max(0, Number(value) || 0);
    this.updateStep(index, step => (step.type === 'delay' ? { ...step, milliseconds: ms } : step));
  }

  onComboRepeatChange(index: number, value: string | number): void {
    const repeat = Math.max(1, Number(value) || 1);
    this.updateStep(index, step => (step.type === 'keyCombo' ? { ...step, repeat } : step));
  }

  onRepeatChange(value: string | number): void {
    this.repeat = Math.max(1, Number(value) || 1);
    this.emit();
  }

  onRepeatDelayChange(value: string | number): void {
    this.repeatDelayMs = Math.max(0, Number(value) || 0);
    this.emit();
  }

  stepLabel(type: KeyboardStepType): string {
    return this.localization.translateKey(this.stepTypeLabels[type]);
  }

  private updateStep(index: number, update: (step: KeyboardStepValue) => KeyboardStepValue): void {
    this.steps = this.steps.map((step, i) => (i === index ? update(step) : step));
    this.emit();
  }

  private createStep(type: KeyboardStepType): KeyboardStepValue {
    switch (type) {
      case 'text':
        return { type: 'text', text: '' };
      case 'delay':
        return { type: 'delay', milliseconds: 100 };
      case 'keyDown':
        return { type: 'keyDown', modifiers: [], key: '' };
      case 'keyUp':
        return { type: 'keyUp', modifiers: [], key: '' };
      case 'keyCombo':
      default:
        return { type: 'keyCombo', modifiers: [], key: '', repeat: 1 };
    }
  }

  private describeStep(step: KeyboardStepValue): string {
    switch (step.type) {
      case 'keyCombo': {
        const combo = formatCombo(step.modifiers, step.key) || '∅';
        return step.repeat && step.repeat > 1 ? `${combo} ×${step.repeat}` : combo;
      }
      case 'text':
        return step.text ? `"${step.text.length > 20 ? `${step.text.slice(0, 20)}…` : step.text}"` : '"…"';
      case 'delay':
        return this.localization.translateKey(
          AppStrings.Forms.KeyboardSequenceEditor.WaitMilliseconds, { milliseconds: step.milliseconds });
      case 'keyDown':
        return `↓ ${formatCombo(step.modifiers, step.key) || '∅'}`;
      case 'keyUp':
        return `↑ ${formatCombo(step.modifiers, step.key) || '∅'}`;
    }
  }

  private emit(): void {
    this.valueChange.emit({
      steps: this.steps,
      repeat: this.repeat,
      repeatDelayMs: this.repeatDelayMs,
    });
  }
}
