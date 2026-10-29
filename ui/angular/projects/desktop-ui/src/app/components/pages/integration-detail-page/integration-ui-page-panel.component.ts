import {
  ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked,
} from '@angular/core';

import type { UiNode, UiNodeEvent } from '@macro-deck/runtime';
import { ButtonComponent, TranslatePipe, UiSessionHandle, UiSessionRejection, UiSessionService } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { LoadingStateComponent } from '../../feedback/loading-state/loading-state.component';
import { UiContentUploader } from '../../ui-render/ui-render-context';
import { UiTreeComponent } from '../../ui-render/ui-tree.component';

@Component({
  selector: 'app-integration-ui-page-panel',
  standalone: true,
  imports: [ButtonComponent, EmptyStateComponent, LoadingStateComponent, TranslatePipe, UiTreeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ui-page-panel">
      @if (!available()) {
        <shared-empty-state
          icon="puzzle"
          [heading]="'macrodeck.app:Integrations.Detail.CapabilityUnavailableHeading' | translate"
          [message]="'macrodeck.app:Integrations.Detail.CapabilityUnavailableMessage' | translate" />
      } @else if (problem(); as problem) {
        <shared-empty-state
          icon="alert-triangle"
          [heading]="'macrodeck.app:Integrations.Detail.CapabilityLoadFailed' | translate"
          [message]="problem.message ?? ''">
          <shared-button variant="secondary" size="compact" (click)="retry()">
            {{ 'macrodeck.app:Integrations.Issues.StartupRetry' | translate }}
          </shared-button>
        </shared-empty-state>
      } @else if (root(); as node) {
        <shared-ui-tree [root]="node" [contentUploader]="contentUploader" (nodeEvent)="onNodeEvent($event)" />
      } @else {
        <shared-loading-state />
      }
    </div>
  `,
  styleUrls: ['./integration-ui-page-panel.component.scss'],
})
export class IntegrationUiPagePanelComponent {
  readonly integrationId = input.required<string>();
  readonly pageId = input.required<string>();
  readonly available = input(true);

  private readonly uiSessions = inject(UiSessionService);

  private readonly session = signal<UiSessionHandle | null>(null);

  protected readonly root = computed<UiNode | null>(() => this.session()?.root() ?? null);

  protected readonly problem = computed<UiSessionRejection | null>(() => {
    const session = this.session();
    return session ? session.fault?.() ?? session.rejection() : null;
  });

  protected readonly contentUploader: UiContentUploader = (nodeId, text) =>
    this.session()?.uploadContent?.(nodeId, text) ?? Promise.resolve(null);

  constructor() {
    effect(() => {
      const integrationId = this.integrationId();
      const pageId = this.pageId();
      const available = this.available();
      untracked(() => {
        if (available) this.open(integrationId, pageId);
        else this.close();
      });
    });

    inject(DestroyRef).onDestroy(() => this.close());
  }

  protected retry(): void {
    if (this.available()) this.open(this.integrationId(), this.pageId());
  }

  protected onNodeEvent(event: UiNodeEvent): void {
    this.session()?.send(event);
  }

  private open(integrationId: string, pageId: string): void {
    this.close();
    this.session.set(this.uiSessions.open({ kind: 'page', integrationId, pageId }));
  }

  private close(): void {
    this.session()?.close();
    this.session.set(null);
  }
}
