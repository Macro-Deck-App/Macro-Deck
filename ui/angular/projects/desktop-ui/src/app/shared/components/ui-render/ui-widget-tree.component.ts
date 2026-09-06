import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, output } from '@angular/core';

import { UiNode, UiNodeEvent, type UiComponentBox } from '@macro-deck/runtime';
import { UiNodeEventBus, UiNodePressedEvent } from './ui-node-event-bus';
import { UiWidgetNodeComponent } from './ui-widget-node.component';

@Component({
  selector: 'shared-ui-widget-tree',
  standalone: true,
  imports: [UiWidgetNodeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [UiNodeEventBus],
  template: `
    @if (root(); as node) {
      <shared-ui-widget-node [node]="node" [box]="box()" />
    }
  `,
  styles: [':host { display: block; width: 100%; height: 100%; }'],
})
export class UiWidgetTreeComponent {
  readonly root = input<UiNode | null>(null);

  readonly box = input<UiComponentBox | null>(null);

  readonly nodeEvent = output<UiNodeEvent>();

  readonly nodePressedChange = output<UiNodePressedEvent>();

  private readonly eventBus = inject(UiNodeEventBus);

  constructor() {
    const destroyRef = inject(DestroyRef);
    const eventsSub = this.eventBus.events$.subscribe(event => this.nodeEvent.emit(event));
    const pressedSub = this.eventBus.pressed$.subscribe(event => this.nodePressedChange.emit(event));
    destroyRef.onDestroy(() => {
      eventsSub.unsubscribe();
      pressedSub.unsubscribe();
    });
  }
}
