import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';
import { AppStrings, IpcIntegrationVariableCapability } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';

const TEMPLATE_PLACEHOLDER = /<([^<>]+)>/;

@Component({
  selector: 'app-variable-capability-row',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display: contents;' },
  templateUrl: './variable-capability-row.component.html',
  styleUrls: ['./variable-capability-row.component.scss'],
})
export class VariableCapabilityRowComponent {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true }) variable!: IpcIntegrationVariableCapability;

  typeLabel(type: 'text' | 'numeric' | 'boolean'): string {
    const S = AppStrings.Scripts;
    const key = type === 'text' ? S.InputTypeText : type === 'numeric' ? S.InputTypeNumeric : S.InputTypeBoolean;
    return this.localization.translateKey(key);
  }

  templateChipLabel(name: string): string {
    const placeholder = TEMPLATE_PLACEHOLDER.exec(name)?.[1];
    return placeholder
      ? this.localization.translateKey(AppStrings.Integrations.Detail.OnePerConfiguredPlaceholder, { placeholder })
      : this.localization.translateKey(AppStrings.Integrations.Detail.OnePerConfiguration);
  }
}
