import { UiComponents } from './ui-component-types';
import { nodeLength, resolveLength } from '../ui-framework/length';
import { UiComponentProperties } from './component-properties';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { paintBarCommon } from './bar-paint';

export const uiRangeBarComponent: UiComponentDefinition = {
  type: UiComponents.RangeBar,

  create(doc: Document) {
    return doc.createElement('div');
  },

  paint(node, ctx) {
    paintBarCommon(node, ctx, false);
  },

  intrinsicMainPx(node, m) {
    return m.horizontal
      ? 0
      : resolveLength(nodeLength(node, UiComponentProperties.Thickness), m.basis, m.crossExtent) ?? 0;
  },
};
