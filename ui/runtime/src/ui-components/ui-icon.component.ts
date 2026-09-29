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

    // A stack leaves an axis open for a child that declares no main size of its own; the icon then takes
    // its own square there, as the contract draws it, rather than the whole basis.
    const known = [ctx.box.width, ctx.box.height].filter((value): value is number => value !== null);
    const edge = resolveLength(nodeLength(node, UiComponentProperties.Size), ctx.basis, ctx.crossExtent)
      ?? (known.length > 0 ? Math.min(...known) : ctx.basis);
    const width = ctx.box.width ?? edge;
    const height = ctx.box.height ?? edge;
    ctx.sizeTo(element, { width, height });

    const name = knownIconName(node);
    if (name === null) {
      ctx.dropPart('glyph');
      return;
    }

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
