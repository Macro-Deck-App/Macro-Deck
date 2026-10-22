import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { WIDGET_REFERENCE_BORDER_RADIUS, WIDGET_REFERENCE_CELL_SIZE } from '@macro-deck/runtime';
import { TranslatePipe, UiTreeWidgetComponent } from '@shared';

@Component({
  selector: 'app-widget-type-preview',
  standalone: true,
  imports: [TranslatePipe, UiTreeWidgetComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './widget-type-preview.component.html',
  styleUrls: ['./widget-type-preview.component.scss'],
})
export class WidgetTypePreviewComponent {
  readonly widgetType = input.required<string>();
  readonly size = input(WIDGET_REFERENCE_CELL_SIZE);

  protected readonly cell = WIDGET_REFERENCE_CELL_SIZE;
  protected readonly radius = WIDGET_REFERENCE_BORDER_RADIUS;
}
