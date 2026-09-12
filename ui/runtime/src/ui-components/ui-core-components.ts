import type { UiComponentDefinition } from '../ui-framework/component-registry';
import { uiButtonComponent } from './ui-button.component';
import { uiChartComponent } from './ui-chart.component';
import { uiImageComponent } from './ui-image.component';
import { uiLayerComponent } from './ui-layer.component';
import { uiListComponent } from './ui-list.component';
import { uiRangeBarComponent } from './ui-range-bar.component';
import { uiSliderComponent } from './ui-slider.component';
import { uiStackComponent } from './ui-stack.component';
import { uiTextComponent } from './ui-text.component';
import { uiTextFieldComponent } from './ui-text-field.component';
import { uiTransformComponent } from './ui-transform.component';
import { uiShapeComponent } from './ui-shape.component';
import { uiIconComponent } from './ui-icon.component';
import { uiGridComponent } from './ui-grid.component';
import { uiGaugeComponent } from './ui-gauge.component';
import { uiToggleComponent } from './ui-toggle.component';
import { uiSegmentedComponent } from './ui-segmented.component';
import { uiDialComponent } from './ui-dial.component';

export const UI_CORE_COMPONENTS: readonly UiComponentDefinition[] = [
  uiStackComponent,
  uiTextComponent,
  uiImageComponent,
  uiRangeBarComponent,
  uiSliderComponent,
  uiButtonComponent,
  uiLayerComponent,
  uiChartComponent,
  uiTextFieldComponent,
  uiListComponent,
  uiTransformComponent,
  uiShapeComponent,
  uiIconComponent,
  uiGridComponent,
  uiGaugeComponent,
  uiToggleComponent,
  uiSegmentedComponent,
  uiDialComponent,
];
