import { LocalizedText } from '../../localization/localized-text';
import { ResultResponse } from './common';
import type { PinScope } from '../../domain/widget.interface';

export type IpcWidgetType = string;

export interface IpcWidget {
  id: string;
  type: IpcWidgetType;
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  data?: string;
  isPinned?: boolean;
  pinScope?: PinScope;
}

export interface CreateWidgetRequest {
  folderId: string;
  type: IpcWidgetType;
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  data?: string;
  sourceWidgetId?: string;
}

export interface CreateWidgetFromApplicationRequest {
  folderId: string;
  positionX: number;
  positionY: number;
  path: string;
}

export interface UpdateWidgetRequest {
  id: string;
  folderId: string;
  type: IpcWidgetType;
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  data?: string;
}

export interface WidgetPositionUpdate {
  id: string;
  positionX: number;
  positionY: number;
  width: number;
  height: number;
}

export interface UpdateWidgetPositionsRequest {
  folderId: string;
  positions: WidgetPositionUpdate[];
}

export interface DeleteWidgetRequest {
  id: string;
  folderId: string;
}

export interface UpdateWidgetStateRequest {
  widgetId: string;
  folderId: string;
  data?: string;
}

export interface SetWidgetPinnedRequest {
  widgetId: string;
  folderId: string;
  pinned: boolean;
  scope?: PinScope;
}

export interface UpdateWidgetDataRequest {
  widgetId: string;
  folderId: string;
  data: string;
}

export interface CreateWidgetResponse extends ResultResponse {
  widget?: IpcWidget;
}

export interface CreateWidgetItem {
  type: IpcWidgetType;
  positionX: number;
  positionY: number;
  width: number;
  height: number;
  data?: string;
  sourceWidgetId?: string;
}

export interface CreateWidgetsRequest {
  folderId: string;
  widgets: CreateWidgetItem[];
  replaceIds?: string[];
}

export interface CreateWidgetsResponse extends ResultResponse {
  widgets?: IpcWidget[];
}

export interface DeleteWidgetsRequest {
  folderId: string;
  ids: string[];
}

export interface DeleteWidgetsResponse extends ResultResponse {}

export interface SetWidgetsPinnedRequest {
  folderId: string;
  widgetIds: string[];
  pinned: boolean;
  scope?: PinScope;
}

export interface SetWidgetsPinnedResponse extends ResultResponse {
  widgets?: IpcWidget[];
}

export interface UpdateWidgetResponse extends ResultResponse {
  widget?: IpcWidget;
}

export interface SetWidgetPinnedResponse extends ResultResponse {
  widget?: IpcWidget;
}

export interface UpdateWidgetPositionsResponse extends ResultResponse {
  widgets?: IpcWidget[];
}

export interface GetWidgetDataSchemasResponse extends ResultResponse {
  schemas: Record<string, unknown>;
}

export interface DeleteWidgetResponse extends ResultResponse {}

export interface UpdateWidgetStateResponse extends ResultResponse {}

export interface UpdateWidgetDataResponse extends ResultResponse {
  widget?: IpcWidget;
}

export interface WidgetCreatedEvent {
  folderId: string;
  widget: IpcWidget;
}

export interface WidgetUpdatedEvent {
  folderId: string;
  widget: IpcWidget;
}

export interface WidgetPositionsUpdatedEvent {
  folderId: string;
  widgets: IpcWidget[];
}

export interface WidgetDeletedEvent {
  widgetId: string;
  folderId: string;
}

export interface WidgetsCreatedEvent {
  folderId: string;
  widgets: IpcWidget[];
}

export interface WidgetsUpdatedEvent {
  folderId: string;
  widgets: IpcWidget[];
}

export interface WidgetsDeletedEvent {
  folderId: string;
  widgetIds: string[];
}

export interface WidgetStateChangedNotification {
  widgetId: string;
  folderId: string;
  data?: string;
}

export interface LabelTextUpdatedEvent {
  widgetId: string;
  state: string;
  text: string | null;
}

export interface WidgetStateUpdatedEvent {
  widgetId: string;
  stateId: string | null;
  stateLabel: LocalizedText;
  states?: { id: string; label: string }[];
}

export interface LabelImagePreviewRequest {
  label?: string;
  fontFaceId?: string;
  fontSize?: number;
  textAlign?: 'left' | 'center' | 'right';
  labelPosition?: 'top' | 'center' | 'bottom';
  labelColor?: string;
  widthCells?: number;
  heightCells?: number;
  scopeRefId?: string;
}
