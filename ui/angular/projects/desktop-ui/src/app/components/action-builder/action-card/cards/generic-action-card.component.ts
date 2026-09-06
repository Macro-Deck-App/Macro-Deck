import { ChangeDetectionStrategy, Component, DestroyRef, Input, computed, effect, inject, signal } from '@angular/core';

import { UiConfigEntryPoints } from '@macro-deck/runtime';
import { LocalizationService, UiSessionHandle, UiSessionService } from '@shared';
import type { ActionBlock } from '@macro-deck/runtime';
import { ActionService } from '../../../../services/action.service';
import { ActionFlowStore } from '../../services/action-flow.store';
import { ActionCardFrameComponent } from '../shell/action-card-frame.component';
import { ParamListComponent } from '../param-list/param-list.component';
import { ProvideButtonStateFieldComponent } from '../param-list/provide-button-state-field.component';
import { ProvideWidgetIconFieldComponent } from '../param-list/provide-widget-icon-field.component';
import { summarizeParameters } from '../summary.util';

@Component({
  selector: 'shared-generic-action-card',
  standalone: true,
  imports: [ActionCardFrameComponent, ParamListComponent, ProvideButtonStateFieldComponent, ProvideWidgetIconFieldComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-action-card-frame
      [block]="block"
      [summary]="summary"
      [collapsible]="hasBody">
      @if (providesButtonState) {
        <shared-provide-button-state-field headerControls [block]="block" />
      }
      @if (providesWidgetIcon) {
        <shared-provide-widget-icon-field headerControls [block]="block" />
      }
      <shared-param-list [block]="block" [root]="root()" [session]="session()" />
    </shared-action-card-frame>
  `,
})
export class GenericActionCardComponent {
  @Input({ required: true }) block!: ActionBlock;

  private readonly store = inject(ActionFlowStore);
  private readonly localization = inject(LocalizationService);
  private readonly actionService = inject(ActionService);
  private readonly uiSessions = inject(UiSessionService);

  protected readonly session = signal<UiSessionHandle | null>(null);

  private readonly expanded = computed(() => !this.hasBody || this.store.isExpanded(this.block.id));

  protected readonly root = computed(() => this.session()?.root() ?? null);

  constructor() {
    effect(() => {
      if (this.expanded()) {
        this.openSession();
      } else {
        this.closeSession();
      }
    });

    inject(DestroyRef).onDestroy(() => this.closeSession());
  }

  get summary(): string {
    return summarizeParameters(this.block.parameters, (key, args) => this.localization.translateKey(key, args));
  }

  get providesButtonState(): boolean {
    return this.store.consumerSupportsStateProvider() &&
      this.store.providesButtonState(this.block.integrationId, this.block.actionId);
  }

  get providesWidgetIcon(): boolean {
    return this.store.consumerSupportsIconProvider() &&
      this.store.providesWidgetIcon(this.block.integrationId, this.block.actionId);
  }

  get hasBody(): boolean {
    return (this.block.parameters?.length ?? 0) > 0;
  }

  private openSession(): void {
    if (this.session()) return;

    const definition = this.actionService.definitionFor(this.block.integrationId, this.block.actionId);
    if (!definition?.supportsConfigUi || !this.block.integrationId || !this.block.actionId) return;

    this.session.set(this.uiSessions.open({
      kind: 'config',
      entryPoint: UiConfigEntryPoints.ActionConfig,
      integrationId: this.block.integrationId,
      actionId: this.block.actionId,
      parameters: Object.fromEntries((this.block.parameters ?? []).map(p => [p.name, p.value])),
      configUiModelVersion: definition.configUiModelVersion ?? 0,
    }));
  }

  private closeSession(): void {
    this.session()?.close();
    this.session.set(null);
  }
}
