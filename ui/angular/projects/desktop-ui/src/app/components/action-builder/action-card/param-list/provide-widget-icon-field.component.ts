import { ChangeDetectionStrategy, Component, Input, computed, inject } from '@angular/core';

import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import type { ActionBlock } from '@macro-deck/runtime';
import { TooltipDirective } from '../../../overlay/tooltip/tooltip.directive';
import { ActionFlowStore } from '../../services/action-flow.store';

// Deliberately driven by store.iconProviderBlockId() and never by local component state, for the same
// reason the state field is: the confirmation it triggers can be cancelled.
@Component({
  selector: 'shared-provide-widget-icon-field',
  standalone: true,
  imports: [TooltipDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="action-header-button provide-icon-button"
      [class.is-providing]="checked()"
      [attr.aria-pressed]="checked()"
      [attr.aria-label]="label()"
      [sharedTooltip]="tooltip()"
      tooltipPlacement="left"
      (pointerdown)="$event.stopPropagation()"
      (click)="onToggle(!checked()); $event.stopPropagation()">
      <span class="icon icon-image icon-xs" aria-hidden="true"></span>
    </button>
  `,
  styles: `
    :host { display: inline-flex; align-items: center; }
    .provide-icon-button {
      opacity: 1;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 1.25rem;
      height: 1.25rem;
      padding: 0;
      background: none;
      border: none;
      border-radius: var(--radius-sm);
      color: var(--color-text-muted);
      cursor: pointer;
      flex-shrink: 0;
      transition: background-color var(--transition-fast), color var(--transition-fast);
    }
    .provide-icon-button:hover { background-color: var(--color-bg-hover); color: var(--color-text-primary); }
    .provide-icon-button.is-providing {
      color: var(--color-accent);
      background-color: var(--color-accent-muted, rgba(99, 102, 241, 0.16));
    }
  `,
})
export class ProvideWidgetIconFieldComponent {
  @Input({ required: true }) block!: ActionBlock;

  private readonly store = inject(ActionFlowStore);
  private readonly localization = inject(LocalizationService);

  protected readonly checked = computed(() => this.store.iconProviderBlockId() === this.block.id);

  protected label(): string {
    const S = AppStrings.ActionBuilder.ProvideWidgetIcon;
    return this.localization.translateKey(this.checked() ? S.StopProviding : S.Provide);
  }

  protected tooltip(): string {
    const S = AppStrings.ActionBuilder.ProvideWidgetIcon;
    return this.localization.translateKey(this.checked() ? S.StopProviding : S.UseActionTooltip);
  }

  protected onToggle(next: boolean): void {
    this.store.requestIconProviderToggle({
      blockId: this.block.id,
      integrationId: this.block.integrationId ?? '',
      actionId: this.block.actionId ?? '',
      actionLabel: this.block.label,
      checked: next,
    });
  }
}
