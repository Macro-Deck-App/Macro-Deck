import { ChangeDetectionStrategy, Component, Input, computed, inject, signal } from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import type { ActionBlockDefinition } from '@macro-deck/runtime';
import { TooltipDirective } from '../../overlay/tooltip/tooltip.directive';
import { ActionFlowStore } from '../services/action-flow.store';

interface ActionCapability {
  id: string;
  icon: string;
  label: string;
  description: string;
}

@Component({
  selector: 'shared-action-capabilities',
  standalone: true,
  imports: [TooltipDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (capabilities().length > 0) {
      <span class="action-capabilities">
        @for (capability of capabilities(); track capability.id) {
          <span
            class="action-capability"
            [sharedTooltip]="capability.description"
            tooltipPlacement="right"
            [attr.aria-label]="capability.label">
            <span class="icon {{ capability.icon }} icon-xs" aria-hidden="true"></span>
            <span class="action-capability-label">{{ capability.label }}</span>
          </span>
        }
      </span>
    }
  `,
  styles: `
    :host { display: contents; }
    .action-capabilities {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-1);
      margin-top: 0.125rem;
    }
    .action-capability {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      padding: 0.0625rem 0.3125rem;
      border-radius: var(--radius-sm);
      background-color: var(--color-bg-hover);
      color: var(--color-text-secondary);
      font-size: var(--text-xs);
      line-height: 1.4;
      white-space: nowrap;
      cursor: default;
    }
    .action-capability-label { font-weight: var(--font-medium); }
  `,
})
export class ActionCapabilitiesComponent {
  @Input({ required: true })
  set definition(value: ActionBlockDefinition) {
    this.definitionInput.set(value);
  }

  private readonly definitionInput = signal<ActionBlockDefinition | null>(null);
  private readonly localization = inject(LocalizationService);
  private readonly store = inject(ActionFlowStore);

  protected readonly capabilities = computed<ActionCapability[]>(() => {
    const definition = this.definitionInput();
    if (!definition) return [];

    const capabilities: ActionCapability[] = [];
    // Advertised only where it could be used: on a surface that cannot consume states, saying the
    // action provides them describes a capability the user has no way to reach from here (issue #741).
    if (definition.providesButtonState && this.store.consumerSupportsStateProvider()) {
      capabilities.push({
        id: 'button-state',
        // The same glyph the action card's provider switch and the button's provider row use, so the
        // capability advertised here and the control that turns it on read as the same thing.
        icon: 'icon-zap',
        label: this.localization.translateKey(AppStrings.ActionBuilder.Picker.StatesCapability),
        description: this.localization.translateKey(AppStrings.ActionBuilder.Picker.StatesCapabilityDescription),
      });
    }
    // Same #741 guard, independently of the state capability above - wired the same way but through
    // its own flag end to end (issue #425), so an action implementing both is advertised with two
    // separate glyphs rather than one that quietly covers both.
    if (definition.providesWidgetIcon && this.store.consumerSupportsIconProvider()) {
      capabilities.push({
        id: 'widget-icon',
        icon: 'icon-image',
        label: this.localization.translateKey(AppStrings.ActionBuilder.Picker.IconCapability),
        description: this.localization.translateKey(AppStrings.ActionBuilder.Picker.IconCapabilityDescription),
      });
    }
    return capabilities;
  });
}
