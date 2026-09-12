import { UiNode } from '../ui-framework/ui-node.interface';
import { UI_ICON_VERSIONS, UI_ICONS_WELL_KNOWN, UiComponents } from './ui-component-types';
import { UiComponentProperties } from './component-properties';
import { nodeString } from '../ui-framework/node-properties.util';
import { nodeLength, resolveLength } from '../ui-framework/length';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { textFillColor } from './style';
import { px } from './px.util';

export function knownIconName(node: UiNode): string | null {
  const name = nodeString(node, UiComponentProperties.Icon);
  return name !== undefined && UI_ICONS_WELL_KNOWN.indexOf(name) >= 0 ? name : null;
}

export const uiIconComponent: UiComponentDefinition = {
  type: UiComponents.Icon,

  version: { minimum: 1, maximum: UI_ICON_VERSIONS.length },

  create(doc: Document) {
    return doc.createElement('div');
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    ctx.setClassName(element, 'widget-icon');
    ctx.sizeTo(element, ctx.box);

    const name = knownIconName(node);
    if (name === null) {
      ctx.dropPart('glyph');
      return;
    }

    const width = ctx.box.width ?? ctx.basis;
    const height = ctx.box.height ?? ctx.basis;
    const edge = resolveLength(nodeLength(node, UiComponentProperties.Size), ctx.basis, ctx.crossExtent)
      ?? Math.min(width, height);
    const glyph = ctx.part('glyph', 'span');
    ctx.setClassName(glyph, `widget-icon-glyph icon icon-${name}`);
    ctx.setStyle(glyph, 'width', px(edge));
    ctx.setStyle(glyph, 'height', px(edge));
    ctx.setStyle(glyph, 'left', px((width - edge) / 2));
    ctx.setStyle(glyph, 'top', px((height - edge) / 2));
    ctx.setStyle(glyph, 'color', textFillColor(node));
  },

  intrinsicMainPx(node, m) {
    return resolveLength(nodeLength(node, UiComponentProperties.Size), m.basis, m.crossExtent) ?? 0;
  },
};
