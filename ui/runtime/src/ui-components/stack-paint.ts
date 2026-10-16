import { UiNode } from '../ui-framework/ui-node.interface';
import { layoutStackChildren } from '../ui-framework/layout';
import { nodeHexColor } from '../ui-framework/length';
import { UiComponentProperties } from './component-properties';
import { nodeResource } from '../ui-framework/ui-resource';
import {
  artworkCrossfades,
  artworkFilter,
  buttonArtworkTransform,
  buttonBackground,
  buttonBorder,
  buttonFit,
  buttonOpacity,
  buttonTakesTileCorner,
  buttonZoom,
  nodeAlign,
  nodeGapPx,
  nodeIsHorizontal,
  nodeJustify,
  nodePaddingPx,
  stackBackground,
  stackClipsStart,
} from './style';
import type { UiComponentContext } from '../ui-framework/component-registry';
import { renderWidgetBorder, WidgetBorderHandle } from '../render/widget-border';
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
import { nodeModifierOwns } from '../render/node-modifiers';

const BUTTON_CORNER_RATIO = 0.12;

interface ButtonArtworkLook {
  fit: string;
  transform: string;
  filter: string | null;
  opacity: number;
  tint: string | null;
}

export interface StackButtonState {
  artwork: ArtworkCrossfadeState<ButtonArtworkLook>;
  border: WidgetBorderHandle | null;
  tintFilter: TintFilterHold;
}

export function createStackButtonState(): StackButtonState {
  return { artwork: createArtworkCrossfadeState(), border: null, tintFilter: createTintFilterHold() };
}

function buttonCornerPx(element: HTMLElement, boxHeight: number | null): number | undefined {
  const height = boxHeight ?? element.clientHeight;
  return height > 0 ? height * BUTTON_CORNER_RATIO : undefined;
}

export function paintStackLayout<TState>(node: UiNode, ctx: UiComponentContext<TState>, isButton: boolean): void {
  const element = ctx.element as HTMLElement;
  const horizontal = nodeIsHorizontal(node);
  const padding = nodePaddingPx(node, ctx);
  const gap = nodeGapPx(node, ctx);

  ctx.setClassName(element, isButton ? 'widget-button' : 'widget-stack');
  ctx.setClass(element, 'widget-artwork-eased', artworkCrossfades(node));
  // The corner is the reader's - from the stylesheet - for a button that is the whole tree and for one
  // that asked for the tile's; an inline value would outrank it. Everything else takes the profile's own
  // fraction of its own height.
  const tileCorner = isButton && !ctx.isTreeRoot && buttonTakesTileCorner(node);
  ctx.setClass(element, 'widget-node-root', isButton && ctx.isTreeRoot);
  ctx.setClass(element, 'widget-tile-corner', tileCorner);
  // Before the corner: a nested button measures its own height, and that read flushes style. With the
  // colour still unset the flush settles on transparent and the button eases in from it on every mount.
  if (!nodeModifierOwns(node, ctx, 'background')) {
    ctx.setStyle(element, 'background', isButton ? buttonBackground(node) : stackBackground(node) ?? null);
  }
  if (!nodeModifierOwns(node, ctx, 'border-radius')) {
    ctx.setStyle(element, 'border-radius',
      isButton && !ctx.isTreeRoot && !tileCorner ? px(buttonCornerPx(element, ctx.box.height)) : null);
  }
  ctx.setStyle(element, 'flex-direction', horizontal ? 'row' : 'column');
  const clipStart = !isButton && stackClipsStart(node);
  ctx.setClass(element, 'widget-stack-clip-start', clipStart);
  ctx.setStyle(element, 'justify-content', clipStart ? 'flex-end' : nodeJustify(node));
  ctx.setStyle(element, 'align-items', nodeAlign(node));
  ctx.setStyle(element, 'gap', px(gap));
  ctx.setStyle(element, 'padding', px(padding));
  ctx.sizeTo(element, ctx.box);

  const entries = layoutStackChildren(node, ctx.box, ctx.basis, padding, gap, horizontal, ctx.registry, clipStart);
  ctx.syncChildren(element, entries);
  ctx.pressTint(node);
}

function paintArtworkTint<TState>(
  ctx: UiComponentContext<TState>,
  look: ButtonArtworkLook,
  tint: string,
  src: string,
): void {
  const layer = ctx.part('artwork-tint', 'div') as HTMLElement;
  ctx.setClassName(layer, 'widget-button-artwork');
  ctx.setAttribute(layer, 'aria-hidden', 'true');
  ctx.setStyle(layer, 'background-color', tint);
  ctx.setStyle(layer, 'mask-image', maskImageUrl(src));
  ctx.setStyle(layer, 'mask-size', look.fit);
  ctx.setStyle(layer, 'mask-position', 'center');
  ctx.setStyle(layer, 'mask-repeat', 'no-repeat');
  ctx.setStyle(layer, 'opacity', String(look.opacity));
  ctx.setStyle(layer, 'transform', look.transform);
  ctx.setStyle(layer, 'filter', look.filter);
}

export function paintButtonArtwork<TState>(
  node: UiNode,
  ctx: UiComponentContext<TState>,
  state: StackButtonState,
): void {
  const edge = Math.max(ctx.box.width ?? 0, ctx.box.height ?? 0);
  const artwork = ctx.host.resourceUrl(
    nodeResource(node, UiComponentProperties.Source),
    edge > 0 ? { displayPx: edge * buttonZoom(node) } : undefined);
  const tint = supportsFilterTint() || supportsMasks() ? nodeHexColor(node, UiComponentProperties.Tint) ?? null : null;
  const look: ButtonArtworkLook = {
    fit: buttonFit(node),
    transform: buttonArtworkTransform(node),
    filter: artworkFilter(node),
    opacity: buttonOpacity(node),
    tint,
  };

  const repaintArtwork = () => {
    const settledLook = state.artwork.settledLook ?? look;
    if (state.artwork.settled === null) {
      ctx.dropPart('artwork');
      ctx.dropPart('artwork-tint');
      releaseTintFilter(state.tintFilter);
    } else {
      const image = ctx.part('artwork', 'img') as HTMLImageElement;
      ctx.setClassName(image, 'widget-button-artwork');
      ctx.setAttribute(image, 'alt', '');
      // A draggable image turns a press-and-move over artwork into a drag of the artwork out of the
      // widget. This attribute is what every engine honours; the sheet's `-webkit-user-drag` is the
      // WebKit-only extra beside it.
      ctx.setAttribute(image, 'draggable', 'false');
      ctx.setStyle(image, 'object-fit', settledLook.fit);
      // Also as an attribute, because an engine without object-fit drops the declaration out of the
      // CSSOM entirely: the value becomes unreadable, and the fallback that stands in for the property
      // on the compatibility floor has nothing left to key off. Costs one attribute.
      ctx.setAttribute(image, 'data-object-fit', settledLook.fit);
      ctx.setAttribute(image, 'aria-hidden', null);
      ctx.setStyle(image, '--widget-artwork-opacity', null);
      ctx.setStyle(image, 'transform', settledLook.transform);
      setImageSource(image, state.artwork.settled);
      const settledTint = settledLook.tint;
      if (settledTint !== null && supportsFilterTint()) {
        ctx.dropPart('artwork-tint');
        const tintFilter = holdTintFilter(state.tintFilter, image.ownerDocument, settledTint);
        ctx.setStyle(image, 'filter', withTintFilter(tintFilter, settledLook.filter));
        ctx.setStyle(image, 'opacity', String(settledLook.opacity));
      } else {
        releaseTintFilter(state.tintFilter);
        ctx.setStyle(image, 'filter', settledLook.filter);
        // The mask fetch has no retry of its own, so it follows the image: until the image has loaded
        // (and after every recovery reload) the untinted image stays visible instead of a blank mask.
        const tinted = settledTint !== null && image.complete && image.naturalWidth > 0;
        ctx.setStyle(image, 'opacity', tinted ? '0' : String(settledLook.opacity));
        if (tinted) {
          paintArtworkTint(ctx, settledLook, settledTint, state.artwork.settled);
        } else {
          ctx.dropPart('artwork-tint');
          if (settledTint !== null) repaintOnLoad(image, ctx);
        }
      }
    }

    if (state.artwork.incoming === null) {
      ctx.dropPart('artwork-incoming');
    } else {
      const incomingLook = state.artwork.incomingLook ?? look;
      const image = ctx.part('artwork-incoming', 'img') as HTMLImageElement;
      ctx.setClassName(image, 'widget-button-artwork widget-artwork-incoming');
      ctx.setAttribute(image, 'alt', '');
      ctx.setAttribute(image, 'draggable', 'false');
      ctx.setAttribute(image, 'aria-hidden', 'true');
      ctx.setStyle(image, 'object-fit', incomingLook.fit);
      // Also as an attribute - see the note on the settled layer above.
      ctx.setAttribute(image, 'data-object-fit', incomingLook.fit);
      // The target opacity travels as a custom property rather than as an inline style: a running
      // animation outranks inline style, so a layer fading in would land on 1 and then jump back to the
      // node's own opacity the moment it was promoted.
      ctx.setStyle(image, '--widget-artwork-opacity', String(incomingLook.opacity));
      ctx.setStyle(image, 'transform', incomingLook.transform);
      ctx.setStyle(image, 'filter', incomingLook.filter);
      setImageSource(image, state.artwork.incoming);
    }
  };

  if (takePromotion(state.artwork)) {
    ctx.dropPart('artwork');
    ctx.renamePart('artwork-incoming', 'artwork');
  }
  const outgoing = state.artwork.settled === null ? null : ctx.part('artwork', 'img') as HTMLImageElement;
  swapArtwork(state.artwork, artwork, look, artworkCrossfades(node) && tint === null, showsBitmap(outgoing),
    () => ctx.repaint());
  repaintArtwork();

  const ring = ctx.isTreeRoot && ctx.host.ownsRootWidgetBorder?.() === true
    ? undefined
    : buttonBorder(node);
  if (ring !== undefined) {
    const overlay = ctx.part('ring', 'div') as HTMLElement;
    ctx.setClassName(overlay, 'widget-button-ring');
    if (state.border === null) {
      state.border = renderWidgetBorder(overlay, () => ctx.host.now(), { insideScaledContent: true });
    }
    state.border.update(ring);
  } else {
    if (state.border !== null) {
      state.border.destroy();
      state.border = null;
    }
    ctx.dropPart('ring');
  }
}

export function releaseStackButtonState(state: StackButtonState): void {
  releaseArtworkCrossfade(state.artwork);
  releaseTintFilter(state.tintFilter);
  if (state.border !== null) {
    state.border.destroy();
    state.border = null;
  }
}
