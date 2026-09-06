import { UiComponents } from './ui-component-types';
import { nodeResource } from '../ui-framework/ui-resource';
import { nodeLength, resolveLength } from '../ui-framework/length';
import { UiComponentProperties } from './component-properties';
import { artworkCrossfades, artworkFilter, buttonArtworkTransform, buttonOpacity } from './style';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import {
  ArtworkCrossfadeState,
  createArtworkCrossfadeState,
  releaseArtworkCrossfade,
  swapArtwork,
} from './artwork-crossfade';
import { px } from './px.util';
import { setImageSource } from './image-recovery';

export interface UiImageState {
  artwork: ArtworkCrossfadeState;
}

export const uiImageComponent: UiComponentDefinition<UiImageState> = {
  type: UiComponents.Image,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiImageState {
    return { artwork: createArtworkCrossfadeState() };
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    ctx.setClassName(element, 'widget-image');
    ctx.setClass(element, 'widget-artwork-eased', artworkCrossfades(node));
    ctx.sizeTo(element, ctx.box);

    const source = ctx.host.resourceUrl(nodeResource(node, UiComponentProperties.Source));
    const edge = resolveLength(nodeLength(node, UiComponentProperties.Size), ctx.basis, ctx.crossExtent);
    const state = ctx.state.artwork;

    const repaintArtwork = () => {
      if (state.settled === null) {
        ctx.dropPart('image');
      } else {
        const image = ctx.part('image', 'img') as HTMLImageElement;
        ctx.setAttribute(image, 'alt', '');
        ctx.setAttribute(image, 'draggable', 'false');
        ctx.setStyle(image, 'width', px(edge));
        ctx.setStyle(image, 'height', px(edge));
        ctx.setStyle(image, 'opacity', String(buttonOpacity(node)));
        ctx.setStyle(image, 'transform', buttonArtworkTransform(node));
        ctx.setStyle(image, 'filter', artworkFilter(node));
        setImageSource(image, state.settled);
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
        ctx.setStyle(image, '--widget-artwork-opacity', String(buttonOpacity(node)));
        // No inline transform: the stylesheet centres this layer with one, and overriding it here would
        // drop the incoming artwork into the box's top-left corner for the length of the fade.
        ctx.setStyle(image, 'filter', artworkFilter(node));
        setImageSource(image, state.incoming);
      }
    };

    swapArtwork(state, source, artworkCrossfades(node), repaintArtwork);
    repaintArtwork();
  },

  release(ctx) {
    releaseArtworkCrossfade(ctx.state.artwork);
  },

  intrinsicMainPx(node, m) {
    return resolveLength(nodeLength(node, UiComponentProperties.Size), m.basis, m.crossExtent) ?? 0;
  },
};
