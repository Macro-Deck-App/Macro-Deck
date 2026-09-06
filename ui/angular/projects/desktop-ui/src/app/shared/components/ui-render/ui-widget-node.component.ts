import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterRenderEffect,
  inject,
  input,
} from '@angular/core';

import { UiNode, UiNodeRenderHandle, UiComponentBox, renderUiNode } from '@macro-deck/runtime';

import { UiNodeEventBus } from './ui-node-event-bus';
import { UiWidgetRenderHostFactory, UiWidgetTickBridge } from './ui-widget-render-host';
import { UiWidgetTreeContext } from './ui-widget-tree-context';

@Component({
  selector: 'shared-ui-widget-node',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The DOM under here is not Angular's. `data-node-id` and `data-node-type` are not bound on the
  // host either: the renderer writes them on the widget element itself, and a second copy out here
  // would give every node two elements answering to its id.
  template: '',
})
export class UiWidgetNodeComponent {
  readonly node = input.required<UiNode>();
  readonly box = input<UiComponentBox | null>(null);
  readonly crossExtent = input<number | null>(null);

  private readonly container = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly treeContext = inject(UiWidgetTreeContext);
  private readonly ticks = inject(UiWidgetTickBridge);
  private readonly hosts = inject(UiWidgetRenderHostFactory);
  private readonly host = this.hosts.bind(inject(UiNodeEventBus), this.treeContext.tileDrawsRootBorder);

  private handle: UiNodeRenderHandle | null = null;
  private timerAttached = false;

  constructor() {
    const destroyRef = inject(DestroyRef);

    afterRenderEffect({
      mixedReadWrite: () => {
        const node = this.node();
        const box = this.box();
        const crossExtent = this.crossExtent();
        const basis = this.treeContext.basis();
        this.ticks.instant();
        this.hosts.localizationVersion();

        this.timerAttached = this.ticks.track(node, destroyRef, this.timerAttached);

        if (this.handle === null) {
          this.handle = renderUiNode(this.container, node, box, crossExtent, basis, this.host);
        } else {
          this.handle.update(node, box, crossExtent, basis);
        }
      },
    });

    destroyRef.onDestroy(() => {
      this.handle?.destroy();
      this.handle = null;
    });
  }
}
