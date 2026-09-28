import { UiComponents } from './ui-component-types';
import { nodeText } from '../ui-framework/node-properties.util';
import { UiComponentProperties } from './component-properties';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { paintTextCommon, textIntrinsicMainPx } from './text-paint';
import { nodeTextSpans } from './text-spans';

export const uiTextComponent: UiComponentDefinition = {
  type: UiComponents.Text,

  create(doc: Document) {
    return doc.createElement('span');
  },

  paint(node, ctx) {
    const text = nodeText(node, UiComponentProperties.Text, ctx.host.localization) ?? '';
    paintTextCommon(node, ctx, null, text, nodeTextSpans(node));
  },

  intrinsicMainPx: textIntrinsicMainPx,
};
