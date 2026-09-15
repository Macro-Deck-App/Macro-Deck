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
  nodeAlign,
  nodeGapPx,
  nodeIsHorizontal,
  nodeJustify,
  nodePaddingPx,
  stackBackground,
} from './style';
import type { UiComponentContext } from '../ui-framework/component-registry';
import { renderWidgetBorder, WidgetBorderHandle } from '../render/widget-border';
import { ArtworkCrossfadeState, swapArtwork } from './artwork-crossfade';
import { px } from './px.util';
import { setImageSource } from './image-recovery';
import { nodeModifierOwns } from '../render/node-modifiers';

const BUTTON_CORNER_RATIO = 0.12;

export interface StackButtonState {
  artwork: ArtworkCrossfadeState;
  border: WidgetBorderHandle | null;
}

export function createStackButtonState(): StackButtonState {
  return { artwork: { settled: null, incoming: null, requestId: 0, promoteTimer: null, promoteFrame: null }, border: null };
}

function buttonCornerPx(element: HTMLElement, boxHeight: number | null): number | undefined {
  const height = boxHeight ?? element.clientHeight;
  return height > 0 ? height * BUTTON_CORNER_RATIO : undefined;
}

export function paintStackLayout<TState>(node: UiNode, ctx: UiComponentContext<TState>, isButton: boolean): void {
  const element = ctx.element as HTMLElement;
  const horizontal = nodeIsHorizontal(node);
  const padding = nodePaddingPx(node, ctx.basis, ctx.crossExtent);
  const gap = nodeGapPx(node, ctx.basis, ctx.crossExtent);

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
  ctx.setStyle(element, 'justify-content', nodeJustify(node));
  ctx.setStyle(element, 'align-items', nodeAlign(node));
  ctx.setStyle(element, 'gap', px(gap));
  ctx.setStyle(element, 'padding', px(padding));
  ctx.sizeTo(element, ctx.box);

  const entries = layoutStackChildren(node, ctx.box, ctx.basis, padding, gap, horizontal, ctx.registry);
  ctx.syncChildren(element, entries);
  ctx.pressTint(node);
}

function supportsMasks(): boolean {
  return typeof CSS !== 'undefined' && typeof CSS.supports === 'function' &&
    (CSS.supports('mask-image', 'url("x")') || CSS.supports('-webkit-mask-image', 'url("x")'));
}

function repaintOnLoad<TState>(image: HTMLImageElement, ctx: UiComponentContext<TState>): void {
  const flagged = image as HTMLImageElement & { __mdTintRepaint?: boolean };
  if (flagged.__mdTintRepaint === true) return;
  flagged.__mdTintRepaint = true;
  image.addEventListener('load', () => ctx.repaint());
}

function paintArtworkTint<TState>(node: UiNode, ctx: UiComponentContext<TState>, tint: string, src: string): void {
  const layer = ctx.part('artwork-tint', 'div') as HTMLElement;
  ctx.setClassName(layer, 'widget-button-artwork');
  ctx.setAttribute(layer, 'aria-hidden', 'true');
  ctx.setStyle(layer, 'background-color', tint);
  ctx.setStyle(layer, 'mask-image', `url("${src.replace(/["\\]/g, '\\$&')}")`);
  ctx.setStyle(layer, 'mask-size', buttonFit(node));
  ctx.setStyle(layer, 'mask-position', 'center');
  ctx.setStyle(layer, 'mask-repeat', 'no-repeat');
  ctx.setStyle(layer, 'opacity', String(buttonOpacity(node)));
  ctx.setStyle(layer, 'transform', buttonArtworkTransform(node));
  ctx.setStyle(layer, 'filter', artworkFilter(node));
}

export function paintButtonArtwork<TState>(
  node: UiNode,
  ctx: UiComponentContext<TState>,
  state: StackButtonState,
): void {
  const artwork = ctx.host.resourceUrl(nodeResource(node, UiComponentProperties.Source));
  const tint = supportsMasks() ? nodeHexColor(node, UiComponentProperties.Tint) ?? null : null;

  const repaintArtwork = () => {
    if (state.artwork.settled === null) {
      ctx.dropPart('artwork');
      ctx.dropPart('artwork-tint');
    } else {
      const image = ctx.part('artwork', 'img') as HTMLImageElement;
      ctx.setClassName(image, 'widget-button-artwork');
      ctx.setAttribute(image, 'alt', '');
      // A draggable image turns a press-and-move over artwork into a drag of the artwork out of the
      // widget. This attribute is what every engine honours; the sheet's `-webkit-user-drag` is the
      // WebKit-only extra beside it.
      ctx.setAttribute(image, 'draggable', 'false');
      ctx.setStyle(image, 'object-fit', buttonFit(node));
      // Also as an attribute, because an engine without object-fit drops the declaration out of the
      // CSSOM entirely: the value becomes unreadable, and the fallback that stands in for the property
      // on the compatibility floor has nothing left to key off. Costs one attribute.
      ctx.setAttribute(image, 'data-object-fit', buttonFit(node));
      ctx.setStyle(image, 'transform', buttonArtworkTransform(node));
      ctx.setStyle(image, 'filter', artworkFilter(node));
      setImageSource(image, state.artwork.settled);
      // The mask fetch has no retry of its own, so it follows the image: until the image has loaded
      // (and after every recovery reload) the untinted image stays visible instead of a blank mask.
      const tinted = tint !== null && image.complete && image.naturalWidth > 0;
      ctx.setStyle(image, 'opacity', tinted ? '0' : String(buttonOpacity(node)));
      if (tinted) {
        paintArtworkTint(node, ctx, tint, state.artwork.settled);
      } else {
        ctx.dropPart('artwork-tint');
        if (tint !== null) repaintOnLoad(image, ctx);
      }
    }

    if (state.artwork.incoming === null) {
      ctx.dropPart('artwork-incoming');
    } else {
      const image = ctx.part('artwork-incoming', 'img') as HTMLImageElement;
      ctx.setClassName(image, 'widget-button-artwork widget-artwork-incoming');
      ctx.setAttribute(image, 'alt', '');
      ctx.setAttribute(image, 'draggable', 'false');
      ctx.setAttribute(image, 'aria-hidden', 'true');
      ctx.setStyle(image, 'object-fit', buttonFit(node));
      // Also as an attribute - see the note on the settled layer above.
      ctx.setAttribute(image, 'data-object-fit', buttonFit(node));
      // The target opacity travels as a custom property rather than as an inline style: a running
      // animation outranks inline style, so a layer fading in would land on 1 and then jump back to the
      // node's own opacity the moment it was promoted.
      ctx.setStyle(image, '--widget-artwork-opacity', String(buttonOpacity(node)));
      ctx.setStyle(image, 'transform', buttonArtworkTransform(node));
      ctx.setStyle(image, 'filter', artworkFilter(node));
      setImageSource(image, state.artwork.incoming);
    }
  };

  swapArtwork(state.artwork, artwork, artworkCrossfades(node) && tint === null, repaintArtwork);
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
  if (state.border !== null) {
    state.border.destroy();
    state.border = null;
  }
}
