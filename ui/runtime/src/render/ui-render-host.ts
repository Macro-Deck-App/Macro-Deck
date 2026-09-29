import { LocalizationTranslator } from '../localization/localized-text';
import { UiNode } from '../ui-framework/ui-node.interface';
import { UiResource, UiResourceHint } from '../ui-framework/ui-resource';
import type { VideoStreamSurface } from '../video-streams/video-stream-client';

export interface UiRenderHost {
  localization: LocalizationTranslator;

  resourceUrl(resource: UiResource | undefined, hint?: UiResourceHint): string | null;

  now(): number;

  culture(): string;

  hourCycle?(): 'h12' | 'h23' | undefined;

  simpleRendering(): boolean;

  fontFamily(faceId: string): string | null;

  fontReady(faceId: string): boolean;

  uiFontKey?(): string;

  emit(node: UiNode, event: string, payload?: unknown): void;

  setPressed?(node: UiNode, pressed: boolean): void;

  /**
   * Whether the surface embedding this tree draws the ring for a `ui.button` that is the tree's own
   * root, instead of the button painting it itself.
   *
   * A deck tile says yes (issue #895): its ring sits beside the transform-scaled tile content and so
   * rasterizes at device resolution, while one painted inside that content is resampled with it and
   * comes out visibly thinner than every other widget's. Absent - a standalone mount, a modal, a
   * conformance fixture - the button paints its own ring as before. Nested buttons always paint their
   * own: only the root's ring has a tile to hand it to.
   */
  ownsRootWidgetBorder?(): boolean;

  videoStreams?(): VideoStreamSurface | null;
}
