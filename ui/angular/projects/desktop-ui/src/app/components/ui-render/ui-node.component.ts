import { ChangeDetectionStrategy, Component, computed, forwardRef, inject, input } from '@angular/core';

import {
  DEFAULT_UI_COMPONENT_REGISTRY,
  UI_COMPONENTS_WELL_KNOWN,
  UI_CONFIG_RENDER_CAPABILITIES,
  UI_MACRODECK_COMPONENTS_WELL_KNOWN,
  UiComponentRange,
  UiNode,
  UiComponentBox,
  isComponentProfileType,
  isUnsupportedResolution,
  resolveRenderableNode,
} from '@macro-deck/runtime';
import { TranslatePipe, UiWidgetNodeComponent } from '@shared';
import { UiChromeComponent } from './ui-chrome.component';
import { UiInputComponent } from './ui-input.component';
import { UiRenderContext } from './ui-render-context';
import { UI_CHROME_TYPES } from './ui-chrome-types.util';

// Both component namespaces, taken from the runtime rather than restated here: the copy this replaced
// had drifted to twelve of the fourteen types, so a text field and a list were routed to the input
// renderer instead of the component one.
const COMPONENT_TYPES: ReadonlySet<string> = new Set([
  ...UI_COMPONENTS_WELL_KNOWN,
  ...UI_MACRODECK_COMPONENTS_WELL_KNOWN,
]);

const RENDER_CAPABILITIES: Readonly<Record<string, UiComponentRange>> = Object.freeze({
  ...DEFAULT_UI_COMPONENT_REGISTRY.capabilities(),
  ...UI_CONFIG_RENDER_CAPABILITIES,
});

@Component({
  selector: 'shared-ui-node',
  standalone: true,
  imports: [
    forwardRef(() => UiChromeComponent),
    forwardRef(() => UiInputComponent),
    forwardRef(() => UiWidgetNodeComponent),
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-node-id]': 'hostId()',
    '[attr.data-node-type]': 'hostType()',
  },
  template: `
    @if (context.isVisible(node())) {
      @if (unsupportedType(); as type) {
        @if (unsupportedIsWidget()) {
          <div class="widget-node-unsupported"
            [attr.data-unsupported-type]="type"
            [attr.title]="type"
            [style.width.px]="box()?.width"
            [style.height.px]="box()?.height"></div>
        } @else {
          <div class="config-node-unsupported" [attr.data-unsupported-type]="type" [attr.title]="type"
            >{{ 'macrodeck.app:UiRender.UnsupportedField' | translate }}</div>
        }
      } @else if (isWidget()) {
        <shared-ui-widget-node [node]="resolvedNode()!" [box]="box()" [crossExtent]="crossExtent()" />
      } @else if (isChrome()) {
        <shared-ui-chrome [node]="resolvedNode()!" />
      } @else {
        <shared-ui-input [node]="resolvedNode()!" />
      }
    }
  `,
  styleUrls: ['./ui-render.component.scss'],
})
export class UiNodeComponent {
  readonly node = input.required<UiNode>();
  readonly box = input<UiComponentBox | null>(null);
  readonly crossExtent = input<number | null>(null);

  protected readonly context = inject(UiRenderContext);

  private readonly resolution = computed(() => resolveRenderableNode(this.node(), RENDER_CAPABILITIES));

  protected readonly unsupportedType = computed(() => {
    const resolution = this.resolution();
    return isUnsupportedResolution(resolution) ? resolution.unsupportedType : null;
  });

  protected readonly unsupportedIsWidget = computed(() => {
    const type = this.unsupportedType();
    return type !== null && isComponentProfileType(type);
  });

  protected readonly resolvedNode = computed<UiNode | null>(() => {
    const resolution = this.resolution();
    return isUnsupportedResolution(resolution) ? null : resolution;
  });

  protected readonly isWidget = computed(() => {
    const node = this.resolvedNode();
    return node !== null && COMPONENT_TYPES.has(node.type);
  });

  protected readonly isChrome = computed(() => {
    const node = this.resolvedNode();
    return node !== null && UI_CHROME_TYPES.has(node.type);
  });

  protected readonly hostId = computed(() => this.resolution().id);

  protected readonly hostType = computed(() => {
    const resolution = this.resolution();
    return isUnsupportedResolution(resolution) ? resolution.unsupportedType : resolution.type;
  });
}
