import { ActionFlow, ConditionExpression } from './action-builder.interface';
import type { WidgetIconRef } from './widget-icon-ref.util';

export type ActionButtonTriggerType = 'onShortPress' | 'onLongPress' | 'onTouchStart' | 'onTouchEnd' | 'onStateChange';

export const DEFAULT_BUTTON_BACKGROUND_COLOR = 'var(--color-accent)';

export const DEFAULT_SLIDER_COLOR = 'var(--color-accent)';

export const WidgetType = {
  ActionButton: 'ActionButton',
  MusicPlayer: 'MusicPlayer',
  Slider: 'Slider',
  Weather: 'Weather',
  HistoryGraph: 'HistoryGraph',
  Clock: 'Clock',
} as const;

export type WidgetType = string;

export const BUILT_IN_WIDGET_TYPES: readonly string[] = Object.values(WidgetType);

export const DEFAULT_WIDGET_BORDER_COLOR = '#ffffff';

export const WIDGET_BORDER_WIDTH = 2;

export const BORDER_ANIMATION_PERIOD_MS = 336_000;

export type WidgetBorderStyle =
  | 'off'
  | 'static'
  | 'heartbeat'
  | 'breathing'
  | 'blink'
  | 'comet'
  | 'ants'
  | 'hue-shift'
  | 'rgb';

export const COLOR_WIDGET_BORDER_STYLES: readonly WidgetBorderStyle[] =
  ['static', 'heartbeat', 'breathing', 'blink', 'comet', 'ants'];

export interface WidgetBorder {
  style?: WidgetBorderStyle;
  color?: string;
}

export interface ResolvedWidgetBorder {
  style: Exclude<WidgetBorderStyle, 'off'>;
  color: string;
}

export function resolveWidgetBorder(border: WidgetBorder | undefined): ResolvedWidgetBorder | null {
  const style = border?.style;
  if (!style || style === 'off') {
    return null;
  }
  return { style, color: border?.color || DEFAULT_WIDGET_BORDER_COLOR };
}

export type WidgetIconFit = 'contain' | 'cover';

export interface WidgetIconDisplay {
  fit?: WidgetIconFit;
  zoom?: number;
  offsetX?: number;
  offsetY?: number;
  opacity?: number;
}

export const DEFAULT_ICON_DISPLAY: Required<WidgetIconDisplay> = {
  fit: 'contain',
  zoom: 100,
  offsetX: 0,
  offsetY: 0,
  opacity: 100,
};

export const ICON_DISPLAY_LIMITS = {
  zoom: { min: 10, max: 400 },
  offset: { min: -100, max: 100 },
  opacity: { min: 0, max: 100 },
} as const;

export interface ButtonState {
  iconId?: string;
  icon?: WidgetIconRef;
  iconDisplay?: WidgetIconDisplay;
  backgroundColor?: string;
  label?: string;
  labelPosition?: 'top' | 'center' | 'bottom';
  labelColor?: string;
  fontFaceId?: string;
  fontSize?: number;
  textAlign?: 'left' | 'center' | 'right';
  border?: WidgetBorder;
}

export interface ButtonStateDefinition {
  id: string;
  label: string;
  appearance?: ButtonState;
}

export interface ButtonStateMappingRule {
  id: string;
  stateId: string;
  when: ConditionExpression;
}

export interface ButtonStateMapping {
  rules: ButtonStateMappingRule[];
  fallbackStateId: string;
}

export interface ButtonStateProvider {
  blockId: string;
  integrationId?: string;
  actionId?: string;
  actionLabel?: string;
  states?: { id: string; label: string }[];
}

export interface ManualStateBackup {
  states: ButtonStateDefinition[];
  stateMapping?: ButtonStateMapping;
  activeStateId?: string;
}

export interface WidgetIconProvider {
  blockId: string;
  integrationId?: string;
  actionId?: string;
  actionLabel?: string;
}

export interface ActionButtonData {
  imageUrl?: string;
  label?: string;
  backgroundColor?: string;
  labelColor?: string;

  fontFaceId?: string;
  fontSize?: number;
  textAlign?: 'left' | 'center' | 'right';
  labelPosition?: 'top' | 'center' | 'bottom';

  border?: WidgetBorder;

  iconId?: string;
  icon?: WidgetIconRef;
  iconDisplay?: WidgetIconDisplay;

  stateMode?: boolean;
  // A press steps to the next state unless this is explicitly false, and never while a provider or a
  // mapping is authoritative.
  cycleStatesOnPress?: boolean;
  states?: ButtonStateDefinition[];
  stateMapping?: ButtonStateMapping;
  stateProvider?: ButtonStateProvider;
  manualStateBackup?: ManualStateBackup;
  activeStateId?: string;

  iconProvider?: WidgetIconProvider;

  flows?: ActionFlow[];
}

export type MusicPlayerCoverStyle = 'small' | 'full';

export interface MusicPlayerData {
  instanceId?: string;
  coverStyle?: MusicPlayerCoverStyle;
  showHeader?: boolean;
  showTitle?: boolean;
  showArtist?: boolean;
  showAlbum?: boolean;
  showTimeline?: boolean;
  border?: WidgetBorder;
  flows?: ActionFlow[];
}

export interface SliderData {
  orientation: 'horizontal' | 'vertical';
  label?: string;
  color?: string;
  labelColor?: string;
  backgroundColor?: string;
  iconId?: string;
  icon?: WidgetIconRef;
  showLabel?: boolean;
  showValue?: boolean;
  border?: WidgetBorder;
  valueVariable?: string;
  min?: number;
  max?: number;
  step?: number;
}

export interface WeatherData {
  instanceId?: string;
  showIcon?: boolean;
  showTemperature?: boolean;
  showCondition?: boolean;
  showLocation?: boolean;
  showForecast?: boolean;
  animateIcon?: boolean;
  forecastDays?: number;
  border?: WidgetBorder;
  flows?: ActionFlow[];
}

export interface HistoryGraphData {
  valueVariable: string;
  title?: string;
  subtitleVariable?: string;
  subtitle?: string;
  showSubtitle?: boolean;
  accentColor?: string;
  maxValue?: number;
  minValue?: number;
  historyLength?: number;
  border?: WidgetBorder;
  flows?: ActionFlow[];
}

export type ClockStyle = 'digital' | 'analog';

export type ClockHourCycle = 'auto' | '12h' | '24h';

export type ClockDateFormat = 'default' | 'day-first' | 'month-first' | 'iso' | 'long';

export type ClockDatePosition = 'below' | 'above' | 'left' | 'right';

export interface ClockData {
  style?: ClockStyle;
  timeZone?: string;
  showLabel?: boolean;
  label?: string;
  showSeconds?: boolean;
  showDate?: boolean;
  showOffset?: boolean;
  backgroundColor?: string;
  textColor?: string;
  hourCycle?: ClockHourCycle;
  leadingZero?: boolean;
  dateFormat?: ClockDateFormat;
  datePosition?: ClockDatePosition;
  border?: WidgetBorder;
  flows?: ActionFlow[];
}

export type WidgetData = ActionButtonData | MusicPlayerData | SliderData | WeatherData | HistoryGraphData
  | ClockData;

export type PinScope = 'Profile' | 'Subtree';

export interface GridWidget {
  id: string;
  folderId: string;
  x: number;
  y: number;
  w: number;
  h: number;
  type: WidgetType;
  data: WidgetData;
  isPinned?: boolean;
  pinScope?: PinScope;
}

export interface GridDimensions {
  cols: number;
  rows: number;
}

export interface CellDimensions {
  cellWidth: number;
  cellHeight: number;
  gap: number;
  padding: number;
}

export const WIDGET_REFERENCE_CELL_SIZE = 120;

export const WIDGET_REFERENCE_GAP = 12;

export const WIDGET_REFERENCE_BORDER_RADIUS = 22;
