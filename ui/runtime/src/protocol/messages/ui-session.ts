export interface OpenConfigUiSessionRequest {
  entryPoint: string;
  integrationId?: string;
  flowId?: string;
  actionId?: string;
  parameters?: Record<string, unknown>;
  folderId?: string;
  folderViewId?: string;
  folderViewConfiguration?: string;
  widgetId?: string;
  // The draft being edited, as JSON object text. Absent starts the surface from the widget's stored
  // configuration, which is not the same thing while the editor holds unsaved changes.
  widgetData?: string;
}

export interface OpenConfigUiSessionResponse {
  accepted: boolean;
  sessionId: string;
  code?: string;
  message?: string;
}

export interface OpenWidgetUiSessionRequest {
  widgetId?: string;
  widgetType?: string;
  data?: unknown;
  ghost?: boolean;
  variableScopeWidgetId?: string;
  sample?: boolean;
}

export interface OpenWidgetUiSessionResponse {
  accepted: boolean;
  sessionId: string;
  code?: string;
  message?: string;
}

export interface OpenFolderUiSessionRequest {
  folderId: string;
}

export interface OpenFolderUiSessionResponse {
  accepted: boolean;
  sessionId: string;
  code?: string;
  message?: string;
  viewId: string;
  navigation?: string;
}

export interface OpenUiPreviewSessionRequest {
  previewId: string;
}

export interface OpenUiPreviewSessionResponse {
  accepted: boolean;
  sessionId: string;
  code?: string;
  message?: string;
}

export interface UiAttachSessionRequest {
  sessionId: string;
}

export interface UiAttachSessionResponse {
  accepted: boolean;
  sessionId: string;
  code?: string;
  message?: string;
  surfaceKind: string;
  sessionMode: string;
  revision: number;
}

export interface UiSendEventRequest {
  sessionId: string;
  nodeId: string;
  name: string;
  data?: unknown;
  revision?: number;
}

export interface UiSendEventResponse {
  accepted: boolean;
  code?: string;
  message?: string;
}

export interface UiSessionTreeUpdatedEvent {
  sessionId: string;
  revision: number;
  tree: unknown;
}

export interface UiSessionPatchedEvent {
  sessionId: string;
  fromRevision: number;
  toRevision: number;
  patch: unknown;
}

export interface UiSessionInvalidatedEvent {
  sessionId: string;
  code: string;
  message: string;
  retryable: boolean;
}

export interface UiSessionClosedEvent {
  sessionId: string;
  reason?: string;
}
