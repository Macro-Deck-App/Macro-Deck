import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService, TranslatePipe } from '@shared';
import type { Variable, VariableType } from '@macro-deck/runtime';
import { VariableBrowserModalComponent } from '../variables/variable-browser-modal.component';

@Component({
  selector: 'shared-variable-picker',
  standalone: true,
  imports: [VariableBrowserModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="vp-trigger"
      [class.active]="open()"
      (click)="toggle()"
      [title]="insertTooltip()">
      <span class="vp-trigger-symbol">{{ '{x}' }}</span>
      <span class="vp-trigger-label">{{ 'macrodeck.app:Variables.Picker.Label' | translate }}</span>
    </button>

    @if (open()) {
      <shared-variable-browser
        mode="pick"
        [heading]="pickHeading()"
        [variables]="variables"
        [acceptedTypes]="acceptedTypes"
        [scopeLabel]="scopeLabelState()"
        [zIndex]="1100"
        (pick)="onPick($event)"
        (close)="open.set(false)" />
    }
  `,
  styleUrls: ['./variable-picker.component.scss'],
})
export class VariablePickerComponent {
  @Input() variables: Variable[] = [];

  @Input() acceptedTypes?: VariableType[];

  private readonly scopeLabelOverride = signal<string | null>(null);
  @Input() set scopeLabel(value: string) { this.scopeLabelOverride.set(value); }

  @Output() pick = new EventEmitter<string>();

  @Output() pickEventParameter = new EventEmitter<string>();

  private readonly localization = inject(LocalizationService);

  readonly scopeLabelState = computed(() =>
    this.scopeLabelOverride() ?? this.localization.translateKey(AppStrings.Variables.ThisWidget));
  readonly insertTooltip = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Picker.InsertTooltip));
  readonly pickHeading = computed(() =>
    this.localization.translateKey(AppStrings.Variables.Picker.PickHeading));

  readonly open = signal(false);

  toggle(): void {
    this.open.update(o => !o);
  }

  onPick(variable: Variable): void {
    if (variable.origin === 'event') {
      this.pickEventParameter.emit(variable.name);
    } else {
      this.pick.emit(variable.name);
    }
    this.open.set(false);
  }
}
