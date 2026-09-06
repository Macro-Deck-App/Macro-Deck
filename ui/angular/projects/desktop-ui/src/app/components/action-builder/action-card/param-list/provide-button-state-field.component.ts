import { ChangeDetectionStrategy, Component, Input, OnChanges, computed, effect, inject, signal } from '@angular/core';

import { AppStrings, ProvidedState } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import type { ActionBlock, ActionBlockParameter } from '@macro-deck/runtime';
import { ButtonStateProviderService } from '../../../../services/button-state-provider.service';
import { TooltipDirective } from '../../../overlay/tooltip/tooltip.directive';
import { ActionFlowStore } from '../../services/action-flow.store';

// Deliberately driven by store.stateProviderBlockId() and never by local component state: the switch
// triggers a confirmation that can be cancelled, and a locally-flipped checkbox would read "on"
// between the click and a cancelled confirm.
@Component({
  selector: 'shared-provide-button-state-field',
  standalone: true,
  imports: [TooltipDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="action-header-button provide-state-button"
      [class.is-providing]="checked()"
      [attr.aria-pressed]="checked()"
      [attr.aria-label]="label()"
      [sharedTooltip]="tooltip()"
      tooltipPlacement="left"
      (pointerdown)="$event.stopPropagation()"
      (click)="onToggle(!checked()); $event.stopPropagation()">
      <span class="icon icon-zap icon-xs" aria-hidden="true"></span>
    </button>
  `,
  styles: `
    :host { display: inline-flex; align-items: center; }
    .provide-state-button {
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
    .provide-state-button:hover { background-color: var(--color-bg-hover); color: var(--color-text-primary); }
    .provide-state-button.is-providing {
      color: var(--color-accent);
      background-color: var(--color-accent-muted, rgba(99, 102, 241, 0.16));
    }
  `,
})
export class ProvideButtonStateFieldComponent implements OnChanges {
  @Input({ required: true }) block!: ActionBlock;

  private readonly store = inject(ActionFlowStore);
  private readonly provider = inject(ButtonStateProviderService);
  private readonly localization = inject(LocalizationService);

  protected readonly states = signal<ProvidedState[]>([]);

  protected readonly checked = computed(() => this.store.stateProviderBlockId() === this.block.id);

  constructor() {
    effect(() => {
      if (this.checked()) {
        void this.refresh();
      } else {
        this.states.set([]);
      }
    });
  }

  ngOnChanges(): void {
    if (this.checked()) {
      void this.refresh();
    }
  }

  protected label(): string {
    const S = AppStrings.ActionBuilder.ProvideButtonState;
    return this.localization.translateKey(this.checked() ? S.StopProviding : S.Provide);
  }

  protected tooltip(): string {
    const S = AppStrings.ActionBuilder.ProvideButtonState;
    if (!this.checked()) return this.localization.translateKey(S.UseActionTooltip);
    const names = this.states().map(state => state.label).join(', ');
    return names
      ? this.localization.translateKey(S.ProvidingNames, { names })
      : this.localization.translateKey(S.ProvidingUnnamed);
  }

  protected onToggle(next: boolean): void {
    this.store.requestStateProviderToggle({
      blockId: this.block.id,
      integrationId: this.block.integrationId ?? '',
      actionId: this.block.actionId ?? '',
      actionLabel: this.block.label,
      checked: next,
    });
  }

  private async refresh(): Promise<void> {
    const integrationId = this.block.integrationId;
    const actionId = this.block.actionId;
    if (!integrationId || !actionId) {
      this.states.set([]);
      return;
    }
    const result = await this.provider.getStates(this.block.id, {
      integrationId,
      actionId,
      parameters: toParameterRecord(this.block.parameters),
    });
    if (result) {
      this.states.set(result);
    }
  }
}

function toParameterRecord(parameters: ActionBlockParameter[] | undefined): Record<string, unknown> {
  const record: Record<string, unknown> = {};
  for (const parameter of parameters ?? []) {
    record[parameter.name] = parameter.value;
  }
  return record;
}
