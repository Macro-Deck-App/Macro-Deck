import { ChangeDetectionStrategy, Component, Input, inject } from '@angular/core';

import { ConfigFlowInstructionDto, LocalizedText, resolveLocalizedText } from '@macro-deck/runtime';
import { LocalizationService, LocalizedTextPipe } from '@shared';
import { CopyValueComponent } from '../copy-value/copy-value.component';

@Component({
  selector: 'shared-config-flow-instructions',
  standalone: true,
  imports: [CopyValueComponent, LocalizedTextPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (instructions.length) {
      <ol class="cfi-list">
        @for (instruction of instructions; track $index) {
          <li class="cfi-item">
            <span class="cfi-index" aria-hidden="true"></span>
            <div class="cfi-content">
              <p class="cfi-text">{{ instruction.text | localizedText }}</p>
              @for (value of instruction.values ?? []; track $index) {
                <shared-copy-value
                  [label]="resolveLabel(value.label)"
                  [value]="value.value"
                  [fallbackZIndex]="fallbackZIndex" />
              }
            </div>
          </li>
        }
      </ol>
    }
  `,
  styleUrls: ['./config-flow-instructions.component.scss'],
})
export class ConfigFlowInstructionsComponent {
  @Input() instructions: ConfigFlowInstructionDto[] = [];
  @Input() fallbackZIndex: number | null = null;

  private readonly localization = inject(LocalizationService);

  protected resolveLabel(label: LocalizedText): string {
    return resolveLocalizedText(label, this.localization);
  }
}
