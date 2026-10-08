import { UiComponents } from './ui-component-types';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { nodeGapPx, nodePaddingPx } from './style';
import { chooseColumns, gridIntrinsicHeight, layoutGridCells } from './grid-layout';
import { nodeLength, resolveLength } from '../ui-framework/length';
import { UiComponentProperties } from './component-properties';
import { px } from './px.util';
import { bindPressGesture, createPressGestureState, PressGestureState, releasePressGesture } from './press-gesture';

export interface UiGridState {
  press: PressGestureState | null;
}

export const uiGridComponent: UiComponentDefinition<UiGridState> = {
  type: UiComponents.Grid,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): UiGridState {
    return { press: null };
  },

  bind(ctx) {
    const press = createPressGestureState(ctx);
    ctx.state.press = press;
    bindPressGesture(ctx.element, ctx, press);
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    ctx.setClassName(element, 'widget-grid');
    const padding = nodePaddingPx(node, ctx);
    const gap = nodeGapPx(node, ctx);
    const minCell = resolveLength(nodeLength(node, UiComponentProperties.MinCellSize), ctx);
    const columns = chooseColumns(node, ctx.box.width, ctx.box.height, padding, gap, minCell);
    ctx.sizeTo(element, ctx.box.height === null && ctx.box.width !== null
      ? { width: ctx.box.width, height: gridIntrinsicHeight(node, ctx.box.width, padding, gap, columns) }
      : ctx.box);
    const cells = layoutGridCells(node, ctx.box.width, ctx.box.height, padding, gap, columns);
    ctx.syncChildren(element, cells.map(cell => ({
      child: cell.child,
      box: { width: cell.width, height: cell.height },
      crossExtent: null,
    })));

    const placed = Array.from(element.children).filter(child => child.hasAttribute('data-node-id'));
    for (let index = 0; index < cells.length && index < placed.length; index++) {
      const child = placed[index] as HTMLElement;
      ctx.setStyle(child, 'left', px(cells[index].left));
      ctx.setStyle(child, 'top', px(cells[index].top));
    }
    ctx.pressTint(node);
  },

  release(ctx) {
    if (ctx.state.press) releasePressGesture(ctx.state.press);
  },

  intrinsicMainPx(node, m) {
    if (m.horizontal || m.crossExtent === null) return 0;
    const padding = nodePaddingPx(node, m);
    const gap = nodeGapPx(node, m);
    const minCell = resolveLength(nodeLength(node, UiComponentProperties.MinCellSize), m);
    return gridIntrinsicHeight(node, m.crossExtent, padding, gap, chooseColumns(node, m.crossExtent, null, padding, gap, minCell));
  },
};
