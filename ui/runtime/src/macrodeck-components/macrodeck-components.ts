import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { macrodeckClockDialComponent } from './macrodeck-clock-dial.component';
import { macrodeckDynamicTextComponent } from './macrodeck-dynamic-text.component';
import { macrodeckProgressBarComponent } from './macrodeck-progress-bar.component';
import { macrodeckProgressTextComponent } from './macrodeck-progress-text.component';

export const MACRO_DECK_COMPONENTS: readonly UiComponentDefinition[] = [
  macrodeckDynamicTextComponent,
  macrodeckClockDialComponent,
  macrodeckProgressBarComponent,
  macrodeckProgressTextComponent,
];
