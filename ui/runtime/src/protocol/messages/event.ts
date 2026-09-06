import { ResultResponse } from './common';

export type {
  EventDefinition,
  EventDefinitionDto,
  GetEventDefinitionsResponse,
} from '../../domain/event-definition.interface';

export interface TriggerEventRequest {
  eventId: string;
  parameters?: Record<string, unknown>;
}

export interface TriggerEventResponse extends ResultResponse {
  queuedSubscriptions: number;
}

export interface ReportFolderChangedRequest {
  folderId: string;
  clientId?: string;
}

export interface ReportFolderChangedResponse {
  success: boolean;
  error?: {
    code: string;
    message: string;
  };
}
