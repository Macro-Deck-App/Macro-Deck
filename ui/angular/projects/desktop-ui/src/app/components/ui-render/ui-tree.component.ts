import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output } from '@angular/core';

import { UiNode, UiNodeEvent, UiComponentBox } from '@macro-deck/runtime';
import { UiNodeEventBus } from '@shared';
import { UiNodeComponent } from './ui-node.component';
import { UiRenderContext } from './ui-render-context';

@Component({
  selector: 'shared-ui-tree',
  standalone: true,
  imports: [UiNodeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [UiNodeEventBus, UiRenderContext],
  template: `
    @if (root(); as node) {
      <shared-ui-node [node]="node" [box]="box()" />
    }
  `,
})
export class UiTreeComponent {
  readonly root = input<UiNode | null>(null);
  readonly box = input<UiComponentBox | null>(null);
  readonly values = input<Record<string, unknown> | null>(null);
  readonly disabled = input(false);
  readonly scopeRefId = input<string | undefined>(undefined);
  readonly unsavedChanges = input(false);
  readonly nodeEvent = output<UiNodeEvent>();

  protected readonly context = inject(UiRenderContext);

  constructor() {
    effect(() => this.context.setRoot(this.root()));
    effect(() => this.context.setValues(this.values()));
    effect(() => this.context.setDisabled(this.disabled()));
    effect(() => this.context.setScopeRefId(this.scopeRefId()));
    effect(() => this.context.setUnsavedChanges(this.unsavedChanges()));

    const subscription = this.context.events$.subscribe(event => this.nodeEvent.emit(event));
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }
}
