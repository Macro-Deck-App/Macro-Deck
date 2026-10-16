import { UiComponents } from './ui-component-types';
import { nodeResource } from '../ui-framework/ui-resource';
import { nodeHexColor, nodeLength, resolveLength } from '../ui-framework/length';
import { UiComponentProperties } from './component-properties';
import { artworkCrossfades, artworkFilter, buttonArtworkTransform, buttonOpacity, buttonZoom } from './style';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import {
  ArtworkCrossfadeState,
  createArtworkCrossfadeState,
  releaseArtworkCrossfade,
  showsBitmap,
  swapArtwork,
  takePromotion,
} from './artwork-crossfade';
import { px } from './px.util';
import { setImageSource } from './image-recovery';
import {
  createTintFilterHold,
  holdTintFilter,
  maskImageUrl,
  releaseTintFilter,
  repaintOnLoad,
  supportsFilterTint,
  supportsMasks,
  TintFilterHold,
  withTintFilter,
} from './artwork-tint';

interface ImageArtworkLook {
  transform: string;
  filter: string | null;
  opacity: number;
  tint: string | null;
}

export interface UiImageState {
  artwork: ArtworkCrossfadeState<ImageArtworkLook>;
  tintFilter: TintFilterHold;
}

export const uiImageComponent: UiComponentDefinition<UiImageState> = {
  type: UiComponents.Image,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiImageState {
    return { artwork: createArtworkCrossfadeState(), tintFilter: createTintFilterHold() };
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    ctx.setClassName(element, 'widget-image');
    ctx.setClass(element, 'widget-artwork-eased', artworkCrossfades(node));
    ctx.sizeTo(element, ctx.box);

    const edge = resolveLength(nodeLength(node, UiComponentProperties.Size), ctx);
    const source = ctx.host.resourceUrl(
      nodeResource(node, UiComponentProperties.Source),
      edge !== undefined && edge > 0 ? { displayPx: edge * buttonZoom(node) } : undefined);
    const state = ctx.state.artwork;
    const tint = supportsFilterTint() || supportsMasks() ? nodeHexColor(node, UiComponentProperties.Tint) ?? null : null;
    const look: ImageArtworkLook = {
      transform: buttonArtworkTransform(node),
      filter: artworkFilter(node),
      opacity: buttonOpacity(node),
      tint,
    };

    const repaintArtwork = () => {
      const settledLook = state.settledLook ?? look;
      if (state.settled === null) {
        ctx.dropPart('image');
        ctx.dropPart('image-tint');
        releaseTintFilter(ctx.state.tintFilter);
      } else {
        const image = ctx.part('image', 'img') as HTMLImageElement;
        ctx.setClassName(image, '');
        ctx.setAttribute(image, 'alt', '');
        ctx.setAttribute(image, 'draggable', 'false');
        ctx.setAttribute(image, 'aria-hidden', null);
        ctx.setStyle(image, '--widget-artwork-opacity', null);
        ctx.setStyle(image, 'width', px(edge));
        ctx.setStyle(image, 'height', px(edge));
        ctx.setStyle(image, 'transform', settledLook.transform);
        setImageSource(image, state.settled);
        const settledTint = settledLook.tint;
        const filterTint = settledTint !== null && supportsFilterTint();
        const tintFilter = holdTintFilter(ctx.state.tintFilter, image.ownerDocument, filterTint ? settledTint : null);
        ctx.setStyle(image, 'filter', withTintFilter(tintFilter, settledLook.filter));
        // The mask fetch has no retry of its own, so the untinted image stays until it has loaded.
        const tinted = !filterTint && settledTint !== null && image.complete && image.naturalWidth > 0;
        ctx.setStyle(image, 'opacity', tinted ? '0' : String(settledLook.opacity));
        if (tinted) {
          const layer = ctx.part('image-tint', 'div') as HTMLElement;
          ctx.setClassName(layer, 'widget-image-tint');
          ctx.setAttribute(layer, 'aria-hidden', 'true');
          ctx.setStyle(layer, 'width', px(edge));
          ctx.setStyle(layer, 'height', px(edge));
          ctx.setStyle(layer, 'background-color', settledTint);
          ctx.setStyle(layer, 'mask-image', maskImageUrl(state.settled));
          ctx.setStyle(layer, 'opacity', String(settledLook.opacity));
          ctx.setStyle(layer, 'filter', settledLook.filter);
        } else {
          ctx.dropPart('image-tint');
          if (settledTint !== null && !filterTint) repaintOnLoad(image, ctx);
        }
      }

      if (state.incoming === null) {
        ctx.dropPart('image-incoming');
      } else {
        const image = ctx.part('image-incoming', 'img') as HTMLImageElement;
        ctx.setClassName(image, 'widget-image-incoming');
        ctx.setAttribute(image, 'alt', '');
        ctx.setAttribute(image, 'draggable', 'false');
        ctx.setAttribute(image, 'aria-hidden', 'true');
        ctx.setStyle(image, 'width', px(edge));
        ctx.setStyle(image, 'height', px(edge));
        const incomingLook = state.incomingLook ?? look;
        ctx.setStyle(image, '--widget-artwork-opacity', String(incomingLook.opacity));
        // No inline transform: the stylesheet centres this layer with one, and overriding it here would
        // drop the incoming artwork into the box's top-left corner for the length of the fade.
        ctx.setStyle(image, 'filter', incomingLook.filter);
        setImageSource(image, state.incoming);
      }
    };

    if (takePromotion(state)) {
      ctx.dropPart('image');
      ctx.renamePart('image-incoming', 'image');
    }
    const outgoing = state.settled === null ? null : ctx.part('image', 'img') as HTMLImageElement;
    swapArtwork(state, source, look, artworkCrossfades(node) && tint === null, showsBitmap(outgoing),
      () => ctx.repaint());
    repaintArtwork();
  },

  release(ctx) {
    releaseArtworkCrossfade(ctx.state.artwork);
    releaseTintFilter(ctx.state.tintFilter);
  },

  intrinsicMainPx(node, m) {
    return resolveLength(nodeLength(node, UiComponentProperties.Size), m) ?? 0;
  },
};
