import { UiNode } from '../ui-framework/ui-node.interface';
import { UiMacroDeckComponents } from './macrodeck-component-types';
import { nodeString } from '../ui-framework/node-properties.util';
import { UiComponentProperties } from '../ui-components/component-properties';
import { formatProgressRun, nodeProgressRef } from './progress';
import type { UiComponentContext, UiComponentDefinition } from '../ui-framework/component-registry';
import { paintTextCommon } from '../ui-components/text-paint';
import { SECOND_MS } from '../ui-components/render-constants';

function progressRun(node: UiNode, ctx: UiComponentContext<unknown>): string {
  const reference = nodeProgressRef(node, UiComponentProperties.Value);
  if (!reference) return '';
  return formatProgressRun(reference, nodeString(node, UiComponentProperties.Format), ctx.host.now(), ctx.host.culture());
}

export const macrodeckProgressTextComponent: UiComponentDefinition = {
  type: UiMacroDeckComponents.ProgressText,

  create(doc: Document) {
    return doc.createElement('span');
  },

  paint(node, ctx) {
    paintTextCommon(node, ctx, 'widget-progress-text', progressRun(node, ctx));
  },

  // No `intrinsicMainPx` of its own, unlike `ui.text` - it defaults to 0 like the rest of the unlisted
  // types, matching this type's absence from the original switch's own cases.

  tickPeriodMs(node) {
    const reference = nodeProgressRef(node, UiComponentProperties.Value);
    return reference && reference.rate !== 0 ? SECOND_MS : null;
  },
};
