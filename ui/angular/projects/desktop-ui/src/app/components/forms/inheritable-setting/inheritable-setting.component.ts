import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';

@Component({
  selector: 'shared-inheritable-setting',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './inheritable-setting.component.html',
  styleUrls: ['./inheritable-setting.component.scss'],
})
export class InheritableSettingComponent {
  private readonly localization = inject(LocalizationService);

  public readonly label = input.required<string>();
  public readonly value = input<number | null>(null);
  public readonly effectiveValue = input<number | null>(null);
  public readonly unit = input('');
  public readonly resettable = input(true);
  public readonly inheritedLabel = input<string | undefined>(undefined);
  public readonly fallbackLabel = input<string | undefined>(undefined);
  public readonly resetLabel = input<string | undefined>(undefined);

  public readonly reset = output<void>();

  private readonly effectiveInheritedLabel = computed(
    () => this.inheritedLabel() ?? this.localization.translateKey(AppStrings.Forms.InheritableSetting.Inherited));

  private readonly effectiveFallbackLabel = computed(
    () => this.fallbackLabel() ?? this.localization.translateKey(AppStrings.Forms.InheritableSetting.Default));

  protected readonly effectiveResetLabel = computed(() => this.resetLabel() ?? this.localization.translateKey(
    AppStrings.Forms.InheritableSetting.ResetToInherited, { label: this.label() }));

  protected readonly valueText = computed(() => {
    const value = this.value();
    if (value !== null) {
      return this.formatNumber(value);
    }

    const effective = this.effectiveValue();
    return effective !== null
      ? `${this.effectiveInheritedLabel()} · ${this.formatNumber(effective)}`
      : this.effectiveFallbackLabel();
  });

  protected readonly isInherited = computed(() => this.value() === null);

  private formatNumber(value: number): string {
    const unit = this.unit();
    return unit ? `${value} ${unit}` : `${value}`;
  }

  onReset(): void {
    if (this.isInherited()) {
      return;
    }
    this.reset.emit();
  }
}
