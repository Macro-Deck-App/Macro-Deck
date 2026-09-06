import { WidgetData, WidgetType } from './widget.interface';

export interface WidgetClipboardEntry {
  type: WidgetType;
  w: number;
  h: number;
  data: WidgetData;
  dx: number;
  dy: number;
  isCut: boolean;
  origin: { widgetId: string; folderId: string } | null;
}
