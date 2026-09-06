import { DestroyRef, Injectable, Signal, inject, signal } from '@angular/core';

import {
  DEFAULT_UI_COMPONENT_REGISTRY,
  UiNode,
  UiRenderHost,
  containsTickingNode,
  uiResourceUrl,
} from '@macro-deck/runtime';

import { FontLoaderService, internalFontFamily } from '../../services/font-loader.service';
import { LocalizationService } from '../../localization';
import { RenderingModeService } from '../../services/rendering-mode.service';
import { ServerClockService } from '../../services/server-clock.service';
import { UiNodeEventBus } from './ui-node-event-bus';
import { UiWidgetResourceBaseUrl } from './ui-widget-resource-base-url';
import { UiWidgetTimeService } from './ui-widget-time.service';

@Injectable({ providedIn: 'root' })
export class UiWidgetRenderHostFactory {
  private readonly localization = inject(LocalizationService);
  private readonly resourceBaseUrl = inject(UiWidgetResourceBaseUrl);
  private readonly serverClock = inject(ServerClockService);
  private readonly renderingMode = inject(RenderingModeService);
  private readonly fontLoader = inject(FontLoaderService);

  private readonly baseUrl = signal<string | null>(this.resourceBaseUrl.current);

  constructor() {
    if (this.baseUrl() === null) void this.resourceBaseUrl.get().then(url => this.baseUrl.set(url));
  }

  readonly localizationVersion: Signal<number> = this.localization.catalogVersion;

  bind(bus: UiNodeEventBus, ownsRootWidgetBorder: () => boolean = () => false): UiRenderHost {
    return {
      localization: this.localization,
      resourceUrl: resource => {
        const base = this.baseUrl();
        return base === null ? null : uiResourceUrl(base, resource);
      },
      now: () => this.serverClock.now(),
      culture: () => this.localization.culture(),
      simpleRendering: () => this.renderingMode.mode() === 'simple',
      fontFamily: faceId => internalFontFamily(faceId),
      // Reading the face's status signal inside the paint is the whole of the readiness gate: the
      // renderer hides text in a face that is still loading, and the read is what brings the frame
      // back once the face lands (issue #457 findings 7/8).
      fontReady: faceId => this.fontLoader.ensureFace(faceId)() !== 'loading',
      emit: (node, name, data) => bus.emit(node, name, data),
      setPressed: (node, pressed) => bus.setPressed(node, pressed),
      ownsRootWidgetBorder,
    };
  }
}

@Injectable({ providedIn: 'root' })
export class UiWidgetTickBridge {
  private readonly time = inject(UiWidgetTimeService);

  readonly instant = this.time.instant;

  track(root: UiNode, destroyRef: DestroyRef, attached: boolean): boolean {
    if (attached || !containsTickingNode(root, DEFAULT_UI_COMPONENT_REGISTRY)) return attached;
    this.time.attach(destroyRef);
    return true;
  }
}
