import { GridRect } from '../grid/grid-layout.util';

export interface WidgetRenderState {
  liveRect: GridRect | null;
  hidden: boolean;
  dimmed: boolean;
  landing: boolean;
}

export const DEFAULT_WIDGET_RENDER_STATE: WidgetRenderState = Object.freeze({
  liveRect: null,
  hidden: false,
  dimmed: false,
  landing: false,
});

export type WidgetGridMode = 'runtime' | 'layout';
