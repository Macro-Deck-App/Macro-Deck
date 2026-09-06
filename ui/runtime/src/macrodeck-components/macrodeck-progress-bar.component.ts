import { UiMacroDeckComponents } from './macrodeck-component-types';
import { UiComponentProperties } from '../ui-components/component-properties';
import { nodeProgressRef } from './progress';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { paintBarCommon } from '../ui-components/bar-paint';
import { SECOND_MS } from '../ui-components/render-constants';

export const macrodeckProgressBarComponent: UiComponentDefinition = {
  type: UiMacroDeckComponents.ProgressBar,

  create(doc: Document) {
    return doc.createElement('div');
  },

  paint(node, ctx) {
    paintBarCommon(node, ctx, true);
  },

  tickPeriodMs(node) {
    const reference = nodeProgressRef(node, UiComponentProperties.Value);
    return reference && reference.rate !== 0 ? SECOND_MS : null;
  },
};
